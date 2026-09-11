# DreamTech Leaderboard

Leaderboard lắp ráp kiểu Lego cho game mobile (uGUI + TextMeshPro, Unity 2022.3 → Unity 6).

Package không biết game nào đang dùng nó. Game cắm vào qua bốn port: backend, chỉ số xếp hạng, nơi lưu "lần xem cuối",
và âm thanh/haptic. Cùng một widget đặt được vào popup, màn riêng, hay một khối trong màn Win.

- Repo (dev project + scene demo): https://github.com/TeamAcMong/unity-leaderboard
- Bối cảnh thiết kế, hướng thẩm mỹ, lỗi của bản tham khảo và cách đã sửa: [`Documentation/DESIGN_NOTES.md`](Documentation/DESIGN_NOTES.md)
- Luật ngắn cho agent: [`CLAUDE.md`](CLAUDE.md)
- Lịch sử phiên bản: [`CHANGELOG.md`](CHANGELOG.md)

## Cài đặt

**Package Manager → `+` → Add package from git URL:**

```
https://github.com/TeamAcMong/unity-leaderboard.git#0.1.0
```

hoặc thêm vào `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.dreamtech.leaderboard": "https://github.com/TeamAcMong/unity-leaderboard.git#0.1.0"
  }
}
```

Tag chỉ chứa nội dung package (tách bằng `git subtree split`), nên cài nhanh và không kéo cả dev project về.

**Yêu cầu:** project phải có **TMP Essential Resources** (Window → TextMeshPro → Import TMP Essential Resources) — prefab mặc
định dùng font `LiberationSans SDF` và shader TMP trong đó.

### Phiên bản đã kiểm

| Unity | uGUI / TextMeshPro | Đã chạy |
|---|---|---|
| 6000.6.0f1 | uGUI 2.6.0 (TMP tích hợp, `com.unity.textmeshpro` 5.0.0 là shim) | 99 test EditMode + 5 test PlayMode của scene demo |
| 2022.3.62f2 | uGUI 1.0.0 + TMP 3.0.7 | 99 test EditMode |
| 2022.3.62f2 | uGUI + TMP 3.2.0-pre.12 | 99 test EditMode + chạy thật trong Icon Match |

TMP 3.2 / uGUI 2.0 đổi `enableWordWrapping` thành `textWrappingMode`. Chỗ duy nhất dùng tới (công cụ Editor) chọn nhánh qua
define `DREAMTECH_LEADERBOARD_TMP_WRAPPING_MODE` do `versionDefines` của asmdef Editor bật.

---

## 1. Có gì trong package

| Assembly | Loại | Tham chiếu | Nội dung |
|---|---|---|---|
| `DreamTech.Leaderboard` (`Runtime/Core`) | C# thuần, `noEngineReferences` | — | Domain (entry, hạng, tier, thay đổi hạng, dựng danh sách row), port, `LeaderboardBoard`, Mock backend |
| `DreamTech.Leaderboard.ViewModel` (`Runtime/ViewModel`) | C# thuần, `noEngineReferences` | Core | `RowState`, `BoardModel`, `RankUpPlanner`, `RevealTimeline`, toán (easing, spring, SmoothDamp), layout list ảo, vị trí banner |
| `DreamTech.Leaderboard.UI` (`Runtime/UI`) | uGUI + TMP | Core, ViewModel | `LeaderboardWidget`, list ảo hoá, row, hiệu ứng, config SO, sink âm thanh/UnityEvent, lưu snapshot PlayerPrefs |
| `DreamTech.Leaderboard.Editor` (`Editor`) | Editor | cả ba | Sinh art/âm tạm, tạo prefab mặc định, validator prefab |
| `DreamTech.Leaderboard.Tests` / `.UI.Tests` (`Tests/Editor`) | EditMode NUnit | — | Test thuần Core + ViewModel, test contract prefab + vòng đời widget |

```
Core  ←  ViewModel  ←  UI  ←  Editor
  ↑                     ↑
  └──── game (glue + composition root) ────┘
```

Toàn bộ logic diễn (ai vượt ai, khi nào hạ cánh, skip về đâu) nằm ở `ViewModel` và test được từng tick không cần Unity.
`UI` chỉ đọc state rồi vẽ.

Asset đi kèm (không có `Resources/`):

| Thư mục | Nội dung |
|---|---|
| `Art/` | Sprite grayscale tint bằng `Image.color` + `LeaderboardTextOutline.mat`. Do `PlaceholderArtGenerator` sinh |
| `Audio/Placeholder/` | 4 WAV tạm (lift, tick, land, fanfare). Do `PlaceholderAudioBaker` sinh |
| `Config/` | `Default*.asset` cho 5 loại config |
| `Prefabs/` | `LeaderboardWidget.prefab`, `LeaderboardEntryRow.prefab` |

---

## 2. Lắp vào game

### Bước 1 — thêm package

Cài bằng git URL như mục **Cài đặt**. Muốn thấy test của package trong Test Runner của game thì thêm vào
`Packages/manifest.json`:

```json
"testables": ["com.dreamtech.leaderboard"]
```

Cài bằng git URL thì thư mục package **chỉ đọc**: các menu sinh art/âm/prefab của package tự xám đi. Muốn chỉnh giao diện thì
tạo **prefab variant** trong `Assets/` của game (xem cách Icon Match làm ở `Assets/IconMatch/Prefabs/UI/Leaderboard/`).

### Bước 2 — viết glue của game

Chỉ cần cài những port mà package chưa có sẵn adapter. Thường là chỉ số xếp hạng:

```csharp
public sealed class LevelsCompletedScoreSource : IScoreSource
{
    public string MetricId => "levels-completed";
    public event Action ScoreChanged;

    public bool TryGetScore(out long score)
    {
        score = /* đọc từ save của game */;
        return score > 0;          // false = chưa có điểm, board sẽ không submit
    }
}
```

Nếu chỉ số đọc được bằng một hàm thì khỏi viết class: `new DelegateScoreSource("coins", () => save.Coins)`.

### Bước 3 — composition root

Một chỗ duy nhất trong game lắp các mảnh lại rồi đăng ký board:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Register()
{
    var settings = boardConfig.CreateSettings();                 // LeaderboardBoardConfig
    var service  = new MockLeaderboardService(mockConfig.CreateOptions());   // đổi backend thật = đổi đúng dòng này
    var board    = new LeaderboardBoard(settings, service, new LevelsCompletedScoreSource(),
                                        new PlayerPrefsLeaderboardSnapshotStore("mygame.leaderboard."));
    LeaderboardBoardRegistry.Register(board);
}
```

`LeaderboardBoardRegistry` chỉ là chỗ nối giữa các assembly (host ở assembly khác tra board theo id). Nhớ
`Unregister(board)` khi quit và trước khi reload assembly trong Editor, nếu không thì board cũ còn nằm lại.

### Bước 4 — host

Host là bất cứ thứ gì chứa widget: popup, màn hình, một khối trong màn Win. Host chịu trách nhiệm ba nhịp:

```csharp
widget.Arm();                                   // 1. TRƯỚC animation mở: xoá row lần trước
widget.SetFeedbackSinks(mySinks);               //    (tuỳ chọn) âm thanh/haptic của game
ShowHostAnimation();

LeaderboardBoardRegistry.TryGet("main", out ILeaderboardBoard board);
var request = new LeaderboardPresentRequest(board, BoardPresentMode.RevealIfPending,
                                            hostReady: showAnimationTask);   // 2. tải ngay, diễn khi host mở xong
LeaderboardPresentResult result = await widget.PresentAsync(request, cancellationToken);

// 3. Khi host bắt đầu ẩn:
widget.Disarm();
```

Ví dụ đầy đủ chạy thật: `Assets/IconMatch/MLGameKitBridge/PopupLeaderboard.cs` (popup của MLGameKit).

---

## 3. Hợp đồng của `LeaderboardWidget`

| Thành viên | Ý nghĩa |
|---|---|
| `Arm()` | Huỷ lần diễn đang dở, xoá list/banner/hạt/trạng thái. Gọi trước khi host bắt đầu hiện, để lúc pop-in không lộ row cũ |
| `PresentAsync(request, token)` | Tải dữ liệu ngay; chỉ bắt đầu intro khi `request.HostReady` xong (tối đa `HostReadyTimeout` = 1.5s). Trả kết quả khi diễn xong, bị skip, bị huỷ hoặc lỗi |
| `Disarm()` | Dừng diễn, giữ nguyên hình cho lúc host fade. Nếu đã chạm nhịp hạ cánh thì ghi nhận "đã xem" |
| `Skip()` | Nhảy về trạng thái cuối, snap camera về row mình rồi vẫn diễn nhịp hạ cánh (pill, shine còn nguyên) |
| `SetFeedbackSinks(list)` | Sink do host cấp, cộng với sink là component trên prefab (`feedbackSinkComponents`) |
| `DebugTimeScale` | 1 = bình thường, 0.1 = chậm 10 lần (chụp màn hình) |
| `PresentFinished` | Event bắn cùng lúc task hoàn tất |

`PresentOutcome`: `Completed`, `Skipped`, `Cancelled`, `Failed`. Khi `Failed`, widget hiện thông báo lỗi + nút Retry.

Widget **không làm gì trong `OnEnable`** (popup của MLGameKit không bao giờ tắt GameObject) và **không tự tra board**:
host truyền board vào, nên phụ thuộc nhìn thấy được.

### Hai chế độ

| `BoardPresentMode` | Hành vi |
|---|---|
| `Browse` | Chỉ xem: cuộn tới row mình, không diễn |
| `RevealIfPending` | So với snapshot "lần xem cuối". Có gì mới thì diễn, không thì nhún nhẹ row mình |

### Diễn gì cho từng loại thay đổi (`RankChangeKind`)

| Kind | Người chơi thấy |
|---|---|
| `RankUp` | Row nhấc lên → leo (người phía trên nhường chỗ sớm) → hạ cánh + shine + pill "▲N". Nhảy hơn `MaxAnimatedPasses` (15) người thì quay số trước |
| `NewEntry` | Row trồi vào chỗ trống + pill NEW, điểm đếm lên |
| `ScoreImproved` | Hạng không đổi, điểm đếm lên + pill BEST |
| `Unchanged` | Nhún nhẹ row mình để mắt tìm thấy nó |
| `RankDown` | Như `Unchanged`: nhún nhẹ, không báo tụt hạng (có hiện "▼N" hay không là quyết định còn mở) |
| `NoLocalEntry` | Người chơi chưa có trên bảng: chỉ hiện list |

Board đang có một lần diễn khác (`TryBeginReveal` trả false) thì widget rơi về `Browse`.

Thưởng theo tier (sunburst, banner, confetti, fanfare) **chỉ** cho top 3 và #1. Banner nằm **dưới** row mình; không đủ
chỗ thì lên trên; không bao giờ che row.

---

## 4. Các port và adapter có sẵn

| Port | Việc | Adapter trong package |
|---|---|---|
| `ILeaderboardService` | Backend: `LocalPlayerId`, `SeasonKey`, `GetLocalEntryAsync`, `SubmitScoreAsync` (giữ điểm tốt nhất), `GetRangeAsync(offset, limit)`. Rank 0-based | `MockLeaderboardService` |
| `IScoreSource` | Chỉ số xếp hạng: `MetricId`, `TryGetScore`, `ScoreChanged` | `ManualScoreSource`, `DelegateScoreSource` |
| `ILeaderboardSnapshotStore` | Lưu `RevealSnapshot(rank, score, seasonKey)` theo board | `InMemoryLeaderboardSnapshotStore`, `PlayerPrefsLeaderboardSnapshotStore` |
| `ILeaderboardFeedbackSink` | Nhịp không-hình-ảnh: `OnBeat(LeaderboardBeat, in LeaderboardBeatContext)` | `AudioSourceLeaderboardFeedback`, `UnityEventLeaderboardFeedback` |

Async ở tầng thuần dùng `System.Threading.Tasks.Task` (không phụ thuộc UniTask). Mọi hàm nhận `CancellationToken`.

`LeaderboardBeat` **chỉ thêm vào cuối**: `RevealStarted=0, Lift=1, SpinTick=2, Pass=3, Land=4, NewEntry=5,
ScoreImproved=6, Celebrate=7, Skipped=8, RevealFinished=9`. Sink của game có thể switch theo số.

### `LeaderboardBoard`

- Tự kéo điểm từ `IScoreSource`. Nơi gọi không truyền điểm, nên không bị buộc vào chỉ số nào.
- Chỉ submit khi điểm nguồn **cao hơn** điểm trên backend. Luôn đọc backend trước khi so, nên submit lỗi sẽ tự thử lại ở lần sync sau.
- `HasUnrevealedChange` không tốn I/O: dùng để quyết định có nên mở popup/hiện chấm đỏ.
- `MarkRevealed` do widget gọi khi chạm nhịp hạ cánh. Đóng host trước lúc đó thì lần mở sau diễn lại.
- Một board chỉ có một lần diễn tại một thời điểm (`TryBeginReveal`).
- Top và đoạn quanh người chơi tải song song (`Task.WhenAll`).

### `MockLeaderboardService`

Sinh `BotCount` bot từ `Seed` (ổn định giữa các lần chạy), điểm phân bố theo `DistributionExponent` giữa `MinScore` và
`MaxScore`. Hoà điểm xếp theo thứ tự đạt điểm; người chơi đứng sau bot cùng điểm. `LatencyMilliseconds = 0` thì chạy
đồng bộ (dùng trong test). Công cụ debug: `SetLocalScore`, `ClearLocalEntry`, `ScoreToReachRank(rank)` (điểm tối thiểu để
đạt hạng đó **hoặc cao hơn**, vì bot hoà điểm có thể làm nhảy qua), `FailNextCall()`.

---

## 5. Cấu hình

| Config (ScriptableObject) | Chứa | Mặc định |
|---|---|---|
| `LeaderboardBoardConfig` | `boardId`, `topCount` (50), `rowsAbove` (4), `rowsBelow` (6), `maximumAnimatedPasses` (15), `podiumSize` (3) | `Config/DefaultLeaderboardBoardConfig.asset` |
| `LeaderboardMotionConfig` | `timeline` (`MotionSettings`: nhịp diễn) + `visuals` (bóng, glow, pill, banner, hạt, loading, `HostReadyTimeout`) | `Config/DefaultLeaderboardMotionConfig.asset` |
| `LeaderboardThemeConfig` | Màu row, màu huy chương, màu tier, màu pill, avatar placeholder, bảng màu confetti | `Config/DefaultLeaderboardThemeConfig.asset` |
| `LeaderboardTextConfig` | Mọi chữ hiển thị (tiêu đề, loading, lỗi, Retry, TOP 3!, YOU'RE #1!, NEW, BEST, tên người chơi...) | `Config/DefaultLeaderboardTextConfig.asset` |
| `MockLeaderboardConfig` | Tuỳ chọn Mock (số bot, seed, dải điểm, độ trễ, season, bộ tên, tên đặc biệt chèn định kỳ) | `Config/DefaultMockLeaderboardConfig.asset` |

Widget để trống config nào thì dùng giá trị mặc định trong code.

Font không nằm trong theme: đặt trên **prefab variant** của row. Tên người chơi nên dùng font riêng có đủ ký tự của
ngôn ngữ mục tiêu **và** ký tự `…` (U+2026). Xem mục 7.

Thông số nhịp diễn quan trọng (`MotionSettings`):

| Field | Mặc định | Ghi chú |
|---|---|---|
| `LiftDuration` / `LiftScale` | 0.22s / 1.05 | Đủ để mắt thấy row tách khỏi list |
| `ClimbSecondsPerRow` | 0.09s, kẹp 0.5–1.4s | Leo xa vẫn dưới 1.5s |
| `MakeRoomAt` | 0.3 | Người phía trên nhường chỗ trước khi mình tới — chồng lấn tối đa 0.3 ô |
| `PassSlideDuration` | 0.26s | Ease-out không nảy |
| `LandDuration` / `LandOvershoot` | 0.32s / 1.3 | "Đặt xuống" có trọng lượng, không bật nảy |
| `SpinDuration` | 0.7s | Quay số khi nhảy quá 15 người |
| `FollowSmoothTime` | 0.12s | Camera `SmoothDamp` |
| `PillPopDuration` / `PillHoldDuration` / `PillRiseDuration` | 0.3 / 1.1 / 0.3s | Pill ▲N / NEW / BEST |

---

## 6. Menu Editor

Ba menu sinh asset chỉ bật khi package **ghi được** (nhúng trong `Packages/` hoặc cài bằng đường dẫn `file:`), tức là trong
dev project. Cài bằng git URL thì chúng xám đi — asset mặc định đã đi kèm package.

| Menu | Việc |
|---|---|
| `Tools/DreamTech/Leaderboard/Create Default Assets (if missing)` | Tạo config + prefab mặc định nếu chưa có. Sau lần đầu, prefab là nguồn sự thật — sửa prefab, đừng sửa bootstrapper |
| `Tools/DreamTech/Leaderboard/Regenerate Placeholder Art` | Luôn ghi đè PNG, áp lại import setting, đóng dấu phiên bản vào `userData` |
| `Tools/DreamTech/Leaderboard/Bake Placeholder Audio` | Ghi 4 WAV tạm |
| `Tools/DreamTech/Leaderboard/Validate Default Prefabs` | Kiểm prefab: ref đã nối, text tên đúng luật, padding đủ |

---

## 7. Bẫy đã gặp

- **Text tên người chơi phải: Ellipsis, KHÔNG Bold, `richText = false`, NoWrap.** Nếu TMP không tìm được ký tự `…` cho
  font/style đang dùng, `TextMeshProUGUI` tự đổi `overflowMode` sang `Truncate` (in cảnh báo "Switching Text Overflow mode
  to Truncate") và giá trị đó ghi thẳng vào component. Bold giả lập làm tăng rủi ro tra ký tự hụt. Row view đặt lại
  Ellipsis + `richText = false` lúc khởi tạo; validator và contract test chặn ở prefab.
- **`richText` bật = tên người chơi điều khiển được giao diện.** Tên `<color=red>Hacker</color>` phải hiện nguyên văn.
- **Popup của MLGameKit không tắt GameObject khi ẩn.** Đừng dựa vào `OnEnable`/`OnDisable` để reset — dùng `Arm`/`Disarm`.
- **Unity 2022 + MLGameKit:** `TextureImportProcessor` của kit ép ASTC 6x6 lên mọi PNG mới import, không lọc đường dẫn.
  Generator áp lại override RGBA32 cho iOS/Android sau khi ghi.
- **Không dùng thư mục có đuôi `~` trong package** nếu repo dùng `.gitignore` global bỏ qua `*~` — thư mục đó sẽ không
  vào git. Tài liệu vì vậy nằm ở `Documentation/`, không phải `Documentation~/`.
- **Trong Editor, lần đầu vẽ một material TMP mới có thể ra khối màu cyan** trong vài khung: đó là shader tạm của async
  shader compilation, không phải lỗi font. Bản build không bị.

---

## 8. Test

Chạy trong Test Runner (EditMode) hoặc qua MCP `tests-run`:

| Assembly | Phủ |
|---|---|
| `DreamTech.Leaderboard.Tests` | Domain (hạng, tier, thay đổi hạng, dựng row, kế hoạch tải), Mock, board (kéo điểm, chỉ submit khi tốt hơn, retry, huỷ, 1 reveal/board), timeline (skip ở **mọi tick** cho 120→108, 900→600, 300→5), toán |
| `DreamTech.Leaderboard.UI.Tests` | Contract prefab, row view dùng lại vẫn diễn đúng pha, vòng đời widget (Arm/Present/Disarm, exception → `Failed`), chữ trên avatar |
| `DreamTech.Leaderboard.Demo.Tests` (chỉ trong dev repo, PlayMode) | Chạy scene demo thật: leo 12 hạng ở cả 3 host, skip trước hạ cánh, đóng trước hạ cánh rồi diễn lại, #1 + người chơi mới, lỗi backend |

Scene đang mở phải được lưu trước khi chạy test qua MCP. Chạy bằng dòng lệnh (dev repo):

```bash
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults TestResults/editmode.xml
Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults TestResults/playmode.xml
```
