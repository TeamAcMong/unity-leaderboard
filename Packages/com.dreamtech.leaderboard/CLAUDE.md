# DreamTech Leaderboard — hướng dẫn cho Claude Code

Leaderboard uGUI + TextMeshPro lắp ráp kiểu Lego. Mã nguồn sống ở repo **TeamAcMong/unity-leaderboard**: nhánh `main` là
dev project Unity 6, package nằm ở `Packages/com.dreamtech.leaderboard`, game cài bằng git URL có tag.

- Cách dùng, hợp đồng host, port, config: `README.md`.
- Vì sao có các luật dưới đây, lỗi của bản tham khảo và cách sửa, kết quả kiểm chứng: `Documentation/DESIGN_NOTES.md`.
- Phát hành: `DEPLOY_UPM_SUBTREE.md` + `deploy.sh` ở gốc repo.
- Bản tham khảo gốc (`Wolffun.Leaderboard`) chỉ còn trên máy dev đầu tiên, trong project Icon Match
  (`Assets/IconMatch/Leaderboard~/`). Không copy code từ đó sang mà không đối chiếu mục 6 của DESIGN_NOTES.

## Phát triển và phát hành

- **Sửa trong dev repo, không sửa trong game.** Game cài bằng git URL thì package chỉ đọc. Muốn thử ngay trong game thì tạm
  trỏ manifest của game sang `file:<đường dẫn repo>/Packages/com.dreamtech.leaderboard`, xong trả lại git URL trước khi commit.
- **Hỗ trợ Unity 2022.3 → Unity 6.** Dev project chạy Unity 6 nên dễ lỡ dùng API mới: không dùng API chỉ có từ 2023.1+
  (`FindAnyObjectByType`, `Awaitable`...) trong `Runtime/` hay `Editor/` của package. API khác nhau giữa TMP 3.0 và TMP 3.2 /
  uGUI 2.0 thì tách bằng `versionDefines` (mẫu: `DREAMTECH_LEADERBOARD_TMP_WRAPPING_MODE` trong asmdef Editor). Code demo
  trong `Assets/Demo` chỉ chạy trên dev project nên được dùng API Unity 6.
- **Trước khi phát hành:** chạy EditMode + PlayMode trên dev project (Unity 6) VÀ EditMode trên một project Unity 2022.3 có
  TMP 3.0.x trỏ `file:` vào package. Pass cả hai mới bump version.
- **Phát hành:** bump `package.json` → cập nhật `CHANGELOG.md` của package + gốc repo → commit + push `main` →
  `./deploy.sh --semver X.Y.Z`. Tag là bất biến: không xoá, không dùng lại số.

## Hướng thẩm mỹ — quan trọng nhất, đừng làm lệch

**Mục tiêu:** "premium smooth" kiểu Royal Match. Animation ngắn, mượt, có kiểm soát, và chỉ có MỘT tiêu điểm là row của
người chơi.

**Đã thử và bị người dùng từ chối (v2, "không hợp").** KHÔNG thêm lại nếu không được yêu cầu rõ:
- hit-stop, rung/shake panel
- squash & stretch, nghiêng row
- xô ngang người bị vượt, flash tối
- bóng mờ đuổi theo (ghost trail), tàn lửa khi leo
- whoosh
- confetti cho mọi lần lên hạng

**Chuyển động:**
- Chiều sâu bằng bóng đổ + scale (`RowState.Lift`, `Scale`), không làm méo hình.
- Vận tốc liên tục: ease in-out, camera `SmoothDamping`, không giật lúc bắt đầu hay dừng.
- Overshoot chỉ ở khoảnh khắc "đến" và phải nhỏ (OutBack khoảng 1.2–1.6).

**Thưởng theo tier:** confetti, sunburst, banner, fanfare CHỈ cho top 3 và #1. Lên hạng thường: shine + pill "▲N" + vài
ngôi sao + "ding". Banner nằm dưới row mình, không bao giờ che row.

**Nhịp độ:** rank-up nhỏ khoảng 1.5s. Luôn skip được bằng chạm. Phần hạ cánh vẫn diễn sau khi skip.

**Khi đổi hướng thẩm mỹ:** hỏi người dùng trước, rồi cập nhật mục này và `Documentation/DESIGN_NOTES.md`.

## Kiến trúc và bất biến

Phụ thuộc một chiều: `Core` ← `ViewModel` ← `UI` ← `Editor`. Core và ViewModel là `noEngineReferences` — **không** thêm
`using UnityEngine` vào đó. Package không biết game nào: không tham chiếu asmdef của game, không `Resources.Load` đường dẫn
của game.

**Port (chỉ thêm, không đổi chữ ký nếu không bump major):** `ILeaderboardService`, `IScoreSource`,
`ILeaderboardSnapshotStore`, `ILeaderboardFeedbackSink`. `LeaderboardBeat` chỉ thêm giá trị vào cuối.

**Trạng thái hiệu ứng nằm trong `RowState`, không nằm trong view.** View chỉ `Render(RowState, clock)`. Thêm hiệu ứng mới
= thêm field + mốc thời gian vào `RowState`, rồi view đọc ra. Không coroutine, không tween trên view.

**Timeline tick từ ngoài.** `RevealTimeline.Tick(deltaTime)` do widget gọi; skip = `RequestSkip` → trạng thái cuối →
xin camera snap → diễn nhịp hạ cánh. Điểm là một track của timeline.

**Snapshot, không pending.** "Đã xem" = `RevealSnapshot(rank, score, seasonKey)`. `RankChange.Resolve` tính lại mỗi lần
mở. `MarkRevealed` chỉ chạy khi chạm nhịp hạ cánh.

**Rank 0-based trong dữ liệu**, hiển thị +1.

**Dựng "trạng thái cũ" từ dữ liệu MỚI** (`RankUpPlanner.Prepare`):
- `passed[0]` là người ngay phía trên mình (bị vượt đầu tiên). Không `Reverse()`.
- Điều kiện vượt: `me.Slot <= startSlot - crossed - makeRoomAt`. Người phía trên nhường chỗ TRƯỚC khi mình tới, nên chồng
  lấn tối đa bằng `MakeRoomAt`.
- Nhảy hơn `MaxAnimatedPasses`: quay số trước, cắt tail rồi append lại sau khi hạ cánh để số hạng không mâu thuẫn.

**Hợp đồng host:** `Arm()` trước animation mở → `PresentAsync(request{board, mode, hostReady})` → `Disarm()` khi host
bắt đầu ẩn. Widget không làm gì trong `OnEnable`, không tự tra board.

**List:** `LeaderboardScrollView` `[DefaultExecutionOrder(100)]` (sau ScrollRect), widget `[DefaultExecutionOrder(110)]`.
Vị trí row = f(`RowState.Slot`). Không LayoutGroup/ContentSizeFitter trong list. Suốt lúc diễn: ghim view row mình
(`PinLocalRow`), tắt cuộn tay. `TryGetRowBounds` tính từ slot, không từ view trong pool.

**Thời gian:** mọi animation dùng `Time.unscaledDeltaTime` × `DebugTimeScale`.

### Module League (`Runtime/League`, đang làm trên nhánh `feature/league`)

Tier theo mùa, vùng lên/xuống, streak thắng, rương theo hạng. Người dùng chốt: **mọi phần phải cắm-rút được như Lego**.
- **Tách assembly.** `DreamTech.Leaderboard.League` (noEngineReferences, chỉ tham chiếu Core) + test riêng
  `DreamTech.Leaderboard.League.Tests`. Core/ViewModel/UI của leaderboard KHÔNG được tham chiếu League. Xoá hai thư mục League là
  gỡ sạch.
- **Ổ cắm = port, lắp ở `LeagueSystemBuilder`:**
  - `ILeagueGroupService` (bot mô phỏng / backend), `ISeasonSchedule`, `ILeagueClock`, `ITrophyRule`, `IWinStreakRule`,
    `ILeagueRewardGranter`, `ILeagueTextStore`, `ILeagueFeatureGate`.
  - Luật nhóm (`ILeagueZoneRule`, `ISeasonOutcomeRule`, `ILeagueRewardTable`) nằm trong `LeagueRules`, dùng chung cho hệ thống
    và dịch vụ mô phỏng để dải zone trên màn hình và kết quả thật không lệch nhau.
  - Port chỉ thêm, không đổi chữ ký.
- **Mọi bản cài `ILeagueGroupService` phải có lớp con của `LeagueGroupServiceContract`** (bộ test hành vi là chính lớp đó, ở
  `Tests/Editor/League/LeagueGroupServiceContractTests.cs`; hợp đồng đầy đủ ở XML doc của `ILeagueGroupService`). Interface không
  đủ.
- **Thắng level ghi ngay, không chờ mạng:** grant cúp có id duy nhất xếp hàng, `FlushPendingTrophiesAsync` gửi lại được. Grant
  của mùa vừa hết vẫn tính cho mùa đó; grant đầu mùa mới khép mùa cũ TRƯỚC rồi mới cộng; grant tới muộn cho mùa đã khép tính
  lại kết quả nếu chưa chốt, đã chốt thì dịch vụ ném `LeagueTrophyGrantRejectedException` và `LeagueSystem` bỏ khỏi hàng chờ +
  bắn `TrophyGrantRejected`. Không có đường nào cúp biến mất im lặng (bài học playtest 0.2.1). Grant mang cửa sổ mùa
  (`LeagueTrophyGrant.Season`) để dịch vụ dựng được sổ cho mùa bị "nhảy qua" (mọi lần gửi trong mùa đó thất bại) — id mùa là
  chuỗi, không suy thứ tự thời gian từ id. Nhận / mất không được tuỳ thứ tự lượt gọi: mọi mùa phía sau còn trống thì chèn sổ
  đúng chỗ và tính lại chuỗi bậc qua các mùa trống; mùa phía sau đã có cúp / kết quả đã chốt thì từ chối `UnknownSeason`.
  Thu gọn sổ (`MaximumStoredResults`) KHÔNG bao giờ bỏ sổ còn kết quả chờ — số sổ được tạm vượt giới hạn, xem / nhận xong tự thu
  gọn. `GetPendingSeasonResultAsync` không trả kết quả của mùa còn cúp chờ gửi, kiểm cả SAU lượt gọi dịch vụ (lượt gọi vắt qua mốc
  đổi mùa).
- **Lượt dùng chung = `SharedOperationRun`.** Lượt gửi của `FlushPendingTrophiesAsync` và lượt tải bảng của `LeagueBoardService`
  chạy bằng token RIÊNG: token của một người gọi chỉ làm người đó thôi chờ (nhận huỷ của chính token đó); lượt chỉ dừng khi mọi
  người chờ đều đã huỷ; lượt đang huỷ / đã xong không nhận thêm người. Không trỏ tới Task gắn token của một người gọi.
- **Mùa chỉ tiến (0.2.1).** Đồng hồ lùi → dịch vụ giữ mùa đang giữ, không mở lại mùa đã khép, mỗi mùa một sổ / một kết quả.
  Game cắm `MonotonicLeagueClock` bọc `OffsetLeagueClock` có lưu; cheat xoá dữ liệu phải `ResetHighWater()`. Hỏi bảng / kết quả
  luôn đẩy hàng chờ cúp trước (`LoadPageAsync`, `GetPendingSeasonResultAsync`, `LeagueBoardService`). Dịch vụ mô phỏng từ chối
  mùa chưa bắt đầu theo đồng hồ của nó (lỗi tạm) và cho lượt gọi bắt đầu trước `DebugResetSimulation` thất bại bằng lỗi tạm
  (`SimulatedLeagueException`, KHÔNG ném `OperationCanceledException` khi token nơi gọi chưa huỷ — widget chỉ coi là huỷ khi token
  của lượt trình bày đã huỷ) — dịch vụ và `LeagueSystem` phải dùng cùng một đồng hồ. `LeagueBoardService` chỉ dùng lại lượt tải
  đang bay của CÙNG mùa; lượt mở trước xong muộn không ghi đè snapshot của lượt mở sau; `SubmitScoreAsync` gửi cúp xong không nhập
  lượt tải mở trước lúc đó. Độ tươi snapshot đo bằng thời gian thực (`Stopwatch`) VÀ giờ League — chỉ giờ League thì
  `MonotonicLeagueClock` đứng yên (giờ máy chậm hơn mốc) làm cache tươi mãi. Dữ liệu mô phỏng chỉ đọc định dạng 2;
  định dạng 1 của 0.2.0 = bắt đầu lại (không viết lại đường đọc/gộp dữ liệu cũ).
- **Main thread, KHÔNG `ConfigureAwait(false)` ở bất cứ đâu trong League.** Sau await, League ghi `ILeagueTextStore` (PlayerPrefs
  chỉ gọi được trên main thread), bắn `StateChanged` cho UI, đọc `ILeagueClock` của host. Một `ConfigureAwait(false)` trong chuỗi
  gọi là đủ hỏng: `UnitySynchronizationContext` không phải context mặc định nên .NET không chạy ngay phần tiếp theo đã bỏ context
  mà đẩy nó sang thread pool, kể cả khi Task hoàn tất trên main thread — lượt gọi bắt đầu từ đó (gửi cúp, hỏi bảng, `Save` của dịch
  vụ mô phỏng) mất main thread luôn (lỗi có từ 0.2.0, sửa trong 0.2.1: mở trang khi còn cúp chờ gửi thất bại vì PlayerPrefs). Test
  chốt: `BackendCompletingOnThreadPool_*` chạy trên `SingleThreadSynchronizationContext` với backend hoàn tất Task trên thread pool.
- **Quà ghi vào hàng chờ trước khi đưa cho game.** Granter trả false thì giữ lại, phát ở `GrantPendingRewards`.
- **Streak:** UI cảnh báo hỏi `WouldLoseStreak(event)`, không tự đoán luật. Tên là `WinStreak` (game host có thể đã có "streak"
  khác).
- **Lưu trạng thái** bằng `LeagueTextRecord` (khoá=giá trị, có `format`). Chuỗi hỏng → bắt đầu lại, không ném lỗi.
- **Mô phỏng tất định:** cúp bot = f(seed, mùa, tier, người chơi, thời điểm), chỉ tăng theo thời gian. Hash dùng FNV-1a, không
  dùng `string.GetHashCode`.

## Text tên người chơi — luật cứng

Ellipsis, KHÔNG Bold (kể cả Bold giả lập qua `fontStyle`), `richText = false`, NoWrap. Font tên phải có `…` (U+2026).
Nếu TMP không tìm được `…` nó tự đổi overflow sang Truncate và ghi vào component. `LeaderboardPrefabValidator.ValidateRow`
và `PackagePrefabContractTests` chặn ở prefab; `LeaderboardEntryView` đặt lại lúc khởi tạo. Sửa row prefab xong phải chạy
lại validator.

## Quy ước code

- **C#:** dùng được trên Unity 2022.3 — không record, không `init`. Tầng thuần dùng BCL `Task`, không UniTask (và không `Awaitable`).
- **Dependency:** chỉ `com.unity.ugui` và `com.unity.textmeshpro`. Không DOTween, PrimeTween, Feel, Odin. Easing ở
  `ViewModel/Math/Easing.cs`, spring ở `DampedSpring`, SmoothDamp ở `SmoothDamping`.
- **Tên đầy đủ, không viết tắt** (không `Lb*`, `idx`, `cnt`). Comment tiếng Việt, tên tiếng Anh.
- **MonoBehaviour/ScriptableObject: tên class = tên file**, một class mỗi file.
- **Không magic number trong setup:** số liệu ở `MotionSettings` / `LeaderboardVisualSettings` / config SO / prefab.
- **Chữ hiển thị** ở `LeaderboardTextConfig`, không hardcode trong logic.
- **Hiệu năng:** không alloc và không set component mỗi frame nếu giá trị không đổi. Chuỗi chỉ tạo khi số đổi.
- **Namespace:** `DreamTech.Leaderboard` (Core), `DreamTech.Leaderboard.ViewModel`, `DreamTech.Leaderboard.UI`,
  `DreamTech.Leaderboard.EditorTools` (Editor).
- **Không thư mục đuôi `~`** trong package (bị `.gitignore` global bỏ qua).
- **Asset sinh bằng code:** `Create Default Assets (if missing)` chỉ tạo khi thiếu; sau đó prefab là nguồn sự thật. Art
  luôn được generator ghi đè; đổi thuật toán thì tăng `GeneratorVersion`.

## Kiểm tra

- EditMode: `DreamTech.Leaderboard.Tests`, `DreamTech.Leaderboard.UI.Tests` (cần `"testables": ["com.dreamtech.leaderboard"]`
  trong manifest). Qua MCP `tests-run`: scene đang mở phải được lưu, và Editor không được đang Play.
- Sửa công thức vượt / dựng trạng thái cũ / skip → chạy `RankUpPlannerInvariantTests` + `RevealTimelineTests`.
- Kiểm chứng thật trong game host (Icon Match): Play từ `Boot.unity`, gọi cheat `Leaderboard*` trên `CheatTool`, đặt
  `LeaderboardSlowMotion(0.1)` rồi chụp Game View. Project để `runInBackground = 0` nên Play đứng hình khi Editor mất
  focus — bật `Application.runInBackground = true` lúc Play.
- Lần đầu vẽ material TMP mới trong Editor có thể ra khối cyan vài khung (async shader compilation) — không phải lỗi.

## Trạng thái và việc còn mở

- **0.1.0:** phát hành đầu tiên. Chạy thật trong Icon Match (popup + cheat), mọi kịch bản ở DESIGN_NOTES mục 7 đã chụp ảnh
  kiểm; test xanh trên Unity 6.6 và Unity 2022.3 (TMP 3.0.7 và 3.2.0-pre.12).
- **Tiếp theo (cần người dùng quyết):** tự submit khi thắng, khối trong màn Win / Home, adapter backend thật, SFX thật,
  rank tụt "▼N", CI chạy test tự động.
- **League:**
  - **0.2.0 (tag, đã vào `main`):** module League + hiển thị bằng widget leaderboard (`LeagueBoardService`).
  - **0.2.1 (nhánh `feature/league`, chưa commit):** sửa lỗi playtest quanh đổi mùa, huỷ lạc của lượt dùng chung, League
    chạy ngoài main thread (xem CHANGELOG). Test: Unity 6000.6.0f1 244/244 EditMode (110 leaderboard + 134 League) + 13/13
    PlayMode; Unity 2022.3.62f2 + TMP 3.0.7 244/244 EditMode.
  - Kế hoạch và design phân tích từ Figma nằm ở project Icon Match: `Assets/IconMatch/Docs/LEAGUE_PLAN.html`.
- **0.4.0 (gộp 0.3.0 chưa từng tag):** host tự trình bày top K (`HostPresentedTopRanks`, bục của Golden Race) + bộ cờ nhịp theo
  một game tham chiếu (curve vẽ tay qua `KeyframeCurve`, tick `Pass` theo đồng hồ, người bị vượt dồn xuống lúc đáp, glow theo đồng
  hồ riêng, nhịp nhẹ khi có điểm mà không đổi hạng). Mọi cờ mặc định tắt; dấu vân tay cờ-tắt (`RevealTimelineFlagOffTests`,
  `LeaderboardFlagOffRenderTests`) không được ghi lại. Repo public: không ghi tên game / class / asset của bản tham chiếu vào
  package.
