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
https://github.com/TeamAcMong/unity-leaderboard.git#0.6.0
```

hoặc thêm vào `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.dreamtech.leaderboard": "https://github.com/TeamAcMong/unity-leaderboard.git#0.6.0"
  }
}
```

Tag chỉ chứa nội dung package (tách bằng `git subtree split`), nên cài nhanh và không kéo cả dev project về.

**Yêu cầu:** project phải có **TMP Essential Resources** (Window → TextMeshPro → Import TMP Essential Resources) — prefab mặc
định dùng font `LiberationSans SDF` và shader TMP trong đó.

### Phiên bản đã kiểm

| Unity | uGUI / TextMeshPro | Đã chạy |
|---|---|---|
| 6000.5.7f1 | uGUI 2.5.0 (TMP tích hợp, `com.unity.textmeshpro` 5.0.0 là shim) | 0.6.0: 487 test EditMode (353 leaderboard + 134 League) + 13 test PlayMode (scene demo + bảng thử League) |
| 6000.6.0f1 | uGUI 2.6.0 (TMP tích hợp, `com.unity.textmeshpro` 5.0.0 là shim) | 0.2.1: 244 test EditMode (110 leaderboard + 134 League) + 13 test PlayMode (scene demo + bảng thử League) |
| 2022.3.62f2 | uGUI 1.0.0 + TMP 3.0.7 | 0.6.0: 487 test EditMode (0.4.0: 403) |
| 2022.3.62f2 | uGUI + TMP 3.2.0-pre.12 | 0.6.0: chạy thật trong Icon Match cùng bộ EditMode của game (0.1.0: 99 test EditMode) |

TMP 3.2 / uGUI 2.0 đổi `enableWordWrapping` thành `textWrappingMode`. Chỗ duy nhất dùng tới (công cụ Editor) chọn nhánh qua
define `DREAMTECH_LEADERBOARD_TMP_WRAPPING_MODE` do `versionDefines` của asmdef Editor bật.

---

## 1. Có gì trong package

| Assembly | Loại | Tham chiếu | Nội dung |
|---|---|---|---|
| `DreamTech.Leaderboard` (`Runtime/Core`) | C# thuần, `noEngineReferences` | — | Domain (entry, hạng, tier, thay đổi hạng, dựng danh sách row), port, `LeaderboardBoard`, Mock backend |
| `DreamTech.Leaderboard.ViewModel` (`Runtime/ViewModel`) | C# thuần, `noEngineReferences` | Core | `RowState`, `BoardModel`, `RankUpPlanner`, `RevealTimeline`, toán (easing, spring, SmoothDamp), layout list ảo, vị trí banner |
| `DreamTech.Leaderboard.UI` (`Runtime/UI`) | uGUI + TMP | Core, ViewModel | `LeaderboardWidget`, list ảo hoá, row, hiệu ứng, config SO, sink âm thanh/UnityEvent, lưu snapshot PlayerPrefs |
| `DreamTech.Leaderboard.League` (`Runtime/League`) | C# thuần, `noEngineReferences` | Core | **Module League**: tier theo mùa, vùng lên/xuống, streak thắng, rương theo hạng, nhóm bot mô phỏng |
| `DreamTech.Leaderboard.League.Unity` (`Runtime/League/Unity`) | UnityEngine | Core, League | `PlayerPrefsLeagueTextStore`, `LeagueDebugPanel` (bảng thử IMGUI) |
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
| `PresentAsync(request, token)` | Tải dữ liệu ngay; chỉ bắt đầu intro khi `request.HostReady` xong (tối đa `HostReadyTimeout` = 1.5s). Trả kết quả khi diễn xong, bị skip, bị huỷ hoặc lỗi. `request.SkipIntro` (0.5.0, mặc định false) = dựng list thẳng ở tư thế đứng, không trượt vào — cho lượt trình bày LẠI trên trang đã mở sẵn; `BoardModel.HasStartedIntro` cho host biết model có đợt trượt không. `visuals.StageListBeforeHostReady` (mặc định tắt) = dựng sẵn list ngay khi dữ liệu về, row người chơi ở ô cũ sát mép trên khung nhìn, mọi row chờ ở đầu đợt trượt; event `ListStaged` báo host đổ phần nó tự vẽ; host sẵn sàng thì canh lại và diễn với CÙNG model |
| `Disarm()` | Dừng diễn, giữ nguyên hình cho lúc host fade. Nếu đã chạm nhịp hạ cánh thì ghi nhận "đã xem" |
| `Skip()` | Nhảy về trạng thái cuối, snap camera về row mình rồi vẫn diễn nhịp hạ cánh (pill, shine còn nguyên) |
| `SetFeedbackSinks(list)` | Sink do host cấp, cộng với sink là component trên prefab (`feedbackSinkComponents`) |
| `DebugTimeScale` | 1 = bình thường, 0.1 = chậm 10 lần (chụp màn hình) |
| `PresentFinished` | Event bắn cùng lúc task hoàn tất |

`PresentOutcome`: `Completed`, `Skipped`, `Cancelled`, `Failed`. Khi `Failed`, widget hiện thông báo lỗi + nút Retry.
`Cancelled` chỉ khi token của lượt trình bày thật sự bị huỷ (host huỷ, `Disarm`, lượt mới thay thế). Backend ném
`OperationCanceledException` mà không ai huỷ lượt đó (huỷ lạc) được coi là lỗi tải → `Failed` + Retry, không đứng mãi ở Loading.

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
| `NewEntry` | Row trồi vào chỗ trống + pill NEW, điểm đếm lên (`NewEntryAccent = false` bỏ pill / shine / loé, giữ cú nở) |
| `ScoreImproved` | Hạng không đổi, điểm đếm lên + pill BEST (`ScoreImprovedPill = false` bỏ pill) |
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
| `LeaderboardMotionConfig` | `timeline` (`MotionSettings`: nhịp diễn) + `visuals` (bóng, glow, pill, banner, hạt, loading, `HostReadyTimeout`, `StageListBeforeHostReady`) | `Config/DefaultLeaderboardMotionConfig.asset` |
| `LeaderboardThemeConfig` | Màu row, màu huy chương, màu tier, màu pill, avatar placeholder, bảng màu confetti | `Config/DefaultLeaderboardThemeConfig.asset` |
| `LeaderboardTextConfig` | Mọi chữ hiển thị (tiêu đề, loading, lỗi, Retry, TOP 3!, YOU'RE #1!, NEW, BEST, tên người chơi...) + `scoreFormat` (0.5.0: chuỗi định dạng số điểm, mặc định `"N0"` = 1,234; `"0"` = 1234) | `Config/DefaultLeaderboardTextConfig.asset` |
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
| `ScoreImprovedPill` / `NewEntryAccent` | true / true | 0.5.0, opt-out: tắt pill BEST của lượt có điểm không đổi hạng / tắt pill NEW + shine + loé của row mới vào bảng. Nhịp cho sink vẫn phát |
| `DeferPodiumApproachPasses` | false | 0.6.0, opt-in, chỉ màn lên bục (`HostPresentedTopRanks`): người bị vượt trong list đứng yên, giữ số hạng cũ tới tick host thả cổng bục, rồi cùng xuống một ô với số hạng thật trong đúng tick đó (theo `PodiumPassSlideDuration`), không nhịp `Pass` |
| `PodiumApproachScrollSpeed` | 0 (tắt) | 0.6.0, opt-in: cú tiếp cận ranh giới bục là một cú cuộn — mở màn canh giữa ô xuất phát (kể cả ô ranh giới), thời lượng = quãng cuộn về đỉnh list / tốc độ (không kẹp), camera và row mình chung một tiến độ (`ClimbCurve`). List báo quãng cuộn cho model (`BoardModel.SetPodiumApproachStartScroll`) |
| `PodiumApproachShortfallRows` | 0 | 0.6.0, opt-in, chỉ khi có cú tiếp cận kiểu cuộn VÀ lúc bắt đầu vùng host trình bày đã khuất hẳn (chỗ cuộn ≥ mép trên ô ranh giới): camera dừng cách đỉnh list bấy nhiêu phần của một bước hàng, thời lượng chỉ tính quãng thật sự cuộn, row mình dời theo nên trên màn vẫn dừng ở chỗ cũ — khớp một game tham chiếu đo quãng cuộn bằng chiều cao model ngắn hơn ảnh thật của vùng bục. List báo hai đầu qua `BoardModel.SetPodiumApproachScrollRange` |
| `PodiumApproachStopOffsetRows` | 0 | 0.6.0, opt-in, chỉ khi có cú tiếp cận kiểu cuộn: row mình dừng lệch khỏi ô ranh giới bấy nhiêu phần của một bước hàng (âm = cao hơn), đứng chờ ở đó tới khi host thả cổng — giữ ảnh thật của list mà vẫn dừng đúng chỗ của một game tham chiếu |
| `HostPresentedRowSkipsQuietPulse` | false | 0.6.0, opt-in: lượt không đổi chỗ của row đang trên bục (ScoreImproved / Unchanged / RankDown) không có pha Bob — host quyết định lúc kết thúc |
| `IntroUsesListBuffer` (+ `IntroBufferAbove` / `IntroBufferBelow` / `IntroBufferBelowRecentred` = 200 / 200 / 300) | false | 0.6.0, opt-in: đợt trượt vào = đúng các ô list ảo hoá đang giữ view (đệm trên / dưới quanh khung nhìn, đệm dưới lớn hơn khi mở ở chỗ cuộn khác 0), row thứ k trễ `IntroRowDelayOffset` + k × `IntroStagger`, `IntroSettleSeconds` = lúc row cuối cửa sổ đậu |
| `CoroutineFrameTiming` | false | 0.6.0, opt-in: các pha theo nhịp khung của coroutine — khung thả cổng vẽ ở giây dt, pha kế bắt đầu ở khung SAU mẫu cuối, tick theo đồng hồ hẹn lại từ khung nó nổ (0,18 s = 11 khung ở 60 Hz). Cùng số khung, nhịp và trạng thái cuối; mẫu vẽ sớm một khung |
| `ClimbTickFrameRate` | 0 (tắt) | 0.6.0, opt-in, chỉ khi `CoroutineFrameTiming` bật: tick của cú leo trên lưới khung của tốc độ này — khoảng làm tròn lên số khung (0,18 s ở 60 = 0,1833 s), tick thứ k ở khung gần k × khoảng đó nhất, dồn từ đầu pha. Đồng hồ thật không đều thì không trôi như cách hẹn lại từ khung nổ |

Lớp nổi của row mình (0.6.0, opt-in): gán `floatingRowLayer` của `LeaderboardScrollView` (prefab variant của game) tới một
RectTransform RỖNG nằm ngoài mask của list — ví dụ anh em ngay sau `ScrollView` trong `ListArea`. Trong cú tiếp cận bục kiểu cuộn,
list đặt lớp đó trùng khung Content mỗi frame và vẽ row mình trong đó, nên màn thấp (khung nhìn ngắn hơn khoảng từ mép trên tới ô
ranh giới) vẫn thấy row tới hết cú tiếp cận. List sở hữu transform của lớp này; đừng đặt thứ gì khác vào đó.

List mở ở đỉnh (0.6.0, opt-in): đặt `LeaderboardScrollView.OpenAtTop = true` trước `PresentAsync` thì lần dựng model không có cú
tiếp cận bục mở list ở chỗ cuộn 0 thay vì canh vào row người chơi — cho màn chỉ-xem-bảng mà host tự hiện row người chơi bằng thanh
ghim riêng. Đợt trượt vào tính theo chỗ cuộn đó.

Dòng mũi tên lên hạng (`LeaderboardRowRankUpStream`, gắn trên prefab row) có bốn field opt-in (0.5.0) cho dáng hệ hạt:
`lifetimeRange` và `riseSpeedRange` (mỗi mũi một tuổi thọ / tốc độ), `prewarmDuration` (dòng chạy sẵn lúc bắt đầu) và
`horizontalDistribution = ProjectedDisc` (rải như hình chiếu đĩa phát). Để mặc định là y hệt 0.4.0.

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
| `DreamTech.Leaderboard.Tests` | Domain (hạng, tier, thay đổi hạng, dựng row, kế hoạch tải), Mock, board (kéo điểm, chỉ submit khi tốt hơn, retry, huỷ, 1 reveal/board), timeline (skip ở **mọi tick** cho 120→108, 900→600, 300→5), cờ nhịp theo game tham chiếu (0.4.0–0.6.0, kể cả cú lên bục: `RevealTimelinePodiumApproachTests`), toán |
| `DreamTech.Leaderboard.UI.Tests` | Contract prefab, row view dùng lại vẫn diễn đúng pha, vòng đời widget (Arm/Present/Disarm, exception → `Failed`, huỷ lạc của backend → `Failed`, token host huỷ → `Cancelled`), chữ trên avatar, phần host trình bày + cú tiếp cận bục / lớp nổi (`LeaderboardPodiumApproachUITests`) |
| `DreamTech.Leaderboard.League.Tests` | Luật League (vùng, kết thúc mùa, streak, cúp, bảng thưởng, lịch mùa), codec lưu trạng thái, `LeagueGroupServiceContract`, nhóm mô phỏng, `LeagueSystem` |
| `DreamTech.Leaderboard.Demo.Tests` (chỉ trong dev repo, PlayMode) | Chạy scene demo thật: leo 12 hạng ở cả 3 host, skip trước hạ cánh, đóng trước hạ cánh rồi diễn lại, #1 + người chơi mới, lỗi backend |

Scene đang mở phải được lưu trước khi chạy test qua MCP. Chạy bằng dòng lệnh (dev repo):

```bash
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults TestResults/editmode.xml
Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults TestResults/playmode.xml
```

---

## 9. Module League

Tier theo mùa (Bronze → Diamond), vùng lên/xuống hạng, streak thắng, rương theo hạng. Nằm ở assembly riêng: không dùng thì
không tốn gì, xoá `Runtime/League` + `Tests/Editor/League` là gỡ sạch, leaderboard vẫn chạy.

```csharp
var rules = new LeagueRules(ladder, rewardTable: rewardTable);
var store = new PlayerPrefsLeagueTextStore();
var cheatClock = new OffsetLeagueClock(new SystemLeagueClock(), store, "clock.offset"); // offset cheat được lưu
var clock = new MonotonicLeagueClock(cheatClock, store, "clock.highWater");           // giờ League không bao giờ lùi
LeagueSystem league = new LeagueSystemBuilder("main", rules, streakLadder)
    .WithGroupService(new SimulatedLeagueGroupService(options, rules, clock, store)) // ← đổi sang backend thật ở đây
    .WithSchedule(new FixedLengthSeasonSchedule(anchorUtc, TimeSpan.FromDays(7)))
    .WithClock(clock)                    // CÙNG đồng hồ với dịch vụ nhóm
    .WithTextStore(store)                // PlayerPrefsLeagueTextStore, hoặc save system của game
    .WithRewardGranter(rewardGranter)    // phát quà vào kho đồ của game
    .WithFeatureGate(featureGate)        // tính năng đã mở chưa
    .WithTrophyRule(new MultipliedTrophyRule(new[] { 10, 15, 20 }))
    .WithWinStreakRule(new StandardWinStreakRule())
    .Build();
LeagueSystemRegistry.Register(league);
```

Game gọi:

| Việc | Gọi gì |
|---|---|
| Thắng level | `RecordLevelWin(new LevelWinContext(level, difficulty))` — ghi ngay, không chờ mạng |
| Thoát / thua hẳn / chơi lại / hồi sinh | `RecordStreakEvent(...)`; popup cảnh báo hỏi `WouldLoseStreak(...)` trước |
| Mở trang League | `LoadPageAsync` — trả dòng kèm vùng lên/xuống và rương từng hạng, số cúp chưa gửi |
| Về Home / mở app | `GetPendingSeasonResultAsync` → popup kết quả → `AcknowledgeSeasonResultAsync` → `ClaimSeasonRewardAsync` |
| Kho đồ vừa sẵn sàng | `GrantPendingRewards()` |

Luật nào cũng thay được bằng một dòng `With…` hoặc tham số của `LeagueRules`: vùng lên/xuống, kết quả mùa, streak, cúp mỗi
trận, bảng thưởng, lịch mùa.

**Đồng hồ và cheat tua giờ.** Mùa tính từ giờ, nên giờ lùi = mùa lùi. Lắp như ví dụ trên:
- `OffsetLeagueClock(inner, store, key)` lưu độ lệch cheat — tắt/mở app không làm giờ League về lại giờ thật. Bản
  `OffsetLeagueClock(inner)` (không lưu) chỉ nên dùng cho test/demo.
- `MonotonicLeagueClock(inner, store, key)` trả `max(giờ bọc trong, mốc cao nhất từng thấy)` và lưu mốc (ghi có tiết chế: chỉ
  khi mốc tiến thêm ít nhất 1 phút). `IsInnerBehind` báo giờ máy đang chậm hơn mốc.
- Cheat "xoá dữ liệu League" phải gọi cả `simulation.DebugResetSimulation()`, `league.DebugClearLocalState()`,
  `cheatClock.Offset = TimeSpan.Zero` **và** `clock.ResetHighWater()` — thiếu bước cuối thì League kẹt ở giờ đã tua tới.
- Dịch vụ mô phỏng tự phòng thủ kể cả khi không cắm đồng hồ không-lùi: đồng hồ lùi thì giữ nguyên mùa đang giữ, không mở lại
  mùa đã khép, mỗi mùa tối đa một kết quả. Dịch vụ và `LeagueSystem` phải dùng **cùng** một đồng hồ: lượt gọi mang mùa chưa bắt
  đầu theo đồng hồ của dịch vụ bị từ chối bằng `SimulatedLeagueException` (lỗi tạm, lần gọi sau đọc lại mùa), còn lượt gọi đang
  chạy dở lúc `DebugResetSimulation()` thất bại bằng `SimulatedLeagueException` (lỗi tạm — không phải
  `OperationCanceledException`, vì token của nơi gọi không bị huỷ; host lọc huỷ thành "người dùng huỷ" sẽ đứng mãi ở Loading) và
  không ghi gì — cheat xoá dữ liệu giữa lúc đang gọi không làm League kẹt ở mùa đã tua tới. Catch của game quanh lượt gọi League
  chỉ nên coi là huỷ khi token của chính nó đã huỷ.
- Dữ liệu mô phỏng lưu định dạng 2 (từ 0.2.1). Dữ liệu định dạng 1 của 0.2.0 bị bỏ, dịch vụ bắt đầu lại từ đầu (bậc khởi đầu,
  không mùa đang giữ, không kết quả cũ). Không hạ package về 0.2.0 được: 0.2.0 không đọc định dạng 2 và cũng bắt đầu lại.

**Cúp quanh lúc đổi mùa.** Cúp thắng đầu mùa mới, và mọi cúp của mùa cũ còn trong hàng chờ, đều được tính. Cúp tới muộn cho
mùa đã khép được tính lại vào kết quả nếu người chơi chưa xem / chưa nhận; đã chốt thì dịch vụ ném
`LeagueTrophyGrantRejectedException`, `LeagueSystem` bỏ grant khỏi hàng chờ và bắn `TrophyGrantRejected` (game ghi log /
analytics ở đó). `GetPendingSeasonResultAsync` trả null khi còn cúp mùa cũ chưa gửi được, hoặc khi kết quả dịch vụ trả về thuộc một
mùa còn cúp chờ gửi (lượt gọi vắt qua mốc đổi mùa), để không hiện kết quả thiếu cúp — lần gọi sau gửi được thì kết quả hiện đủ cúp.
Kết quả chưa xem / chưa nhận không bao giờ bị bỏ khi dịch vụ mô phỏng thu gọn sổ theo `SimulatedLeagueOptions.MaximumStoredResults`
(số sổ được tạm vượt giới hạn, xem / nhận xong thì tự thu gọn).
Grant mang cửa sổ mùa lúc thắng (`LeagueTrophyGrant.Season`): thắng ở một mùa mà mọi lần gửi trong mùa đó đều thất bại, lần gửi
được đầu tiên rơi vào mùa sau, thì dịch vụ dựng một mùa đã khép cho mùa bị "nhảy qua" đó, có kết quả riêng — không mất cúp. Kết
quả không phụ thuộc thứ tự lượt gọi: trang / bảng đã kịp tải mùa sau (dịch vụ đã giữ, hoặc đã mở rồi khép, những mùa **trống** phía
sau) thì sổ vẫn được chèn đúng chỗ và các mùa trống phía sau được tính lại theo bậc mới. Mùa phía sau đã có cúp (hoặc kết quả đã
chốt) thì grant bị từ chối `UnknownSeason` và báo qua `TrophyGrantRejected`.
`FlushPendingTrophiesAsync` gọi chồng nhau dùng chung một lượt gửi; token của một người gọi chỉ làm người đó thôi chờ, lượt gửi chạy
tiếp cho người khác (chỉ dừng khi mọi người chờ đều huỷ) — luồng kết quả mùa bị huỷ không làm trang League hỏi bảng mùa mới trước
khi cúp mùa cũ gửi xong. Lượt tải bảng của `LeagueBoardService` cũng vậy: HUD và popup cùng tải, HUD bị tắt thì popup vẫn nhận bảng.
`LeagueBoardService.SubmitScoreAsync` gửi cúp xong thì không dùng lại lượt tải mở trước lúc đó (entry trả về luôn có cúp vừa gửi).
Bảng trong cache chỉ "tươi" 0.5 giây, đo bằng cả thời gian thực lẫn giờ League: giờ máy chậm hơn mốc của `MonotonicLeagueClock`
(giờ League đứng yên) không làm bảng cũ được dùng mãi.

**Viết adapter backend mới:** cài `ILeagueGroupService` rồi tạo một lớp con của `LeagueGroupServiceContract` trong test —
đó là hợp đồng hành vi (ghi ở XML doc của `ILeagueGroupService`): sort + rank liên tục, cộng cúp idempotent, cúp gửi trễ sau
khi hết mùa vẫn tính cho mùa cũ, cúp đầu mùa mới không mất, mọi grant mùa cũ xếp hàng đều tính, grant tới muộn tính lại kết
quả chưa chốt / bị từ chối khi đã chốt, mỗi mùa một kết quả, xem + nhận idempotent, đồng hồ lùi không mở lại mùa đã khép, grant
của mùa bị nhảy qua được tính cho mùa đó (cả khi dịch vụ còn giữ mùa trước, đã sang mùa sau, có mùa trống đã khép nằm giữa, hay mùa
đầu tiên nó giữ là mùa trống phía sau) và bị từ chối khi mùa phía sau đã có cúp.
Task của adapter hoàn tất trên thread nào cũng được (SDK mạng hay hoàn tất trên thread nền): game gọi League từ main thread, League
không dùng `ConfigureAwait(false)`, nên phần sau mỗi await (ghi PlayerPrefs, `StateChanged`, đọc đồng hồ) quay về main thread. Phần
việc chạy trên thread nền BÊN TRONG adapter thì không được đụng API chỉ-main-thread của Unity.

**Bảng thử khi chưa có UI:** `LeagueDebugPanel.Create(league, simulation, clock, featureGate)` bày mọi thao tác ra nút IMGUI
và vẽ bảng nhóm. Nút "Xoá dữ liệu" xoá luôn mốc của `MonotonicLeagueClock` (truyền qua overload
`Create(league, simulation, clock, monotonicClock, featureGate)`, hoặc tự nhận khi `league.Clock` là đồng hồ không-lùi).
Giờ máy + offset đang chậm hơn mốc thì "→ hết mùa" vẫn tua tới đúng cuối mùa theo giờ League, "Tua +1h/+6h" bù phần chậm để giờ
League tiến đúng số giờ (`AdvanceLeagueTime`; `AdvanceTime` cộng thẳng vào offset).
Dùng trong dev project (`Assets/Demo/LeagueDemo.unity`) hoặc bật bằng cheat ngay trong game thật.
