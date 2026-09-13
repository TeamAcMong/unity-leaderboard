# Changelog

Mọi thay đổi đáng kể của package ghi ở đây. Định dạng theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
phiên bản theo [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-09-13

Thêm module **League**: tier theo mùa, vùng lên/xuống hạng, streak thắng, rương theo hạng. Nằm trong assembly riêng, không
đụng gì tới leaderboard đang chạy — xoá thư mục `Runtime/League` + `Tests/Editor/League` là gỡ sạch.

### Added
- Domain thuần C# (`DreamTech.Leaderboard.League`): `LeagueLadder`, `LeagueZoneBands`, `SeasonWindow`, `SeasonResult`,
  `WinStreakLadder` / `WinStreakState`, `LeagueRewardPackage`, `LeagueGroupSnapshot` (dùng lại `LeaderboardEntry`).
- Luật cắm-rút, mỗi cái một interface + bản mặc định: `ILeagueZoneRule` (`CountLeagueZoneRule`), `ISeasonOutcomeRule`
  (`ZoneSeasonOutcomeRule`), `IWinStreakRule` (`StandardWinStreakRule`), `ITrophyRule` (`MultipliedTrophyRule`),
  `ILeagueRewardTable` (`RankBracketRewardTable`, `EmptyLeagueRewardTable`), `ISeasonSchedule` (`FixedLengthSeasonSchedule`).
- Port của host: `ILeagueGroupService`, `ILeagueClock`, `ILeagueTextStore`, `ILeagueRewardGranter`, `ILeagueFeatureGate`.
- `LeagueSystem` + `LeagueSystemBuilder` + `LeagueSystemRegistry`: ghi thắng level không chờ mạng, hàng chờ cúp gửi lại được
  (idempotent theo grant id), tải trang kèm vùng và rương từng hạng, luồng khép mùa, nhận rương không phát trùng.
- `SimulatedLeagueGroupService`: nhóm bot chạy offline, tất định theo (seed, mùa, tier, người chơi, thời điểm); cúp bot chỉ
  tăng theo thời gian. Có sẵn công cụ debug: đặt cúp, đổi bậc, leo hạng (theo hiện tại hoặc giữ tới hết mùa), lỗi mạng giả lập.
- Adapter: `SystemLeagueClock`, `OffsetLeagueClock` (cheat tua giờ), `ManualLeagueClock`, `InMemoryLeagueTextStore`,
  `DelegateLeagueRewardGranter`, `DeferredLeagueRewardGranter`, `ManualLeagueFeatureGate`, `DelegateLeagueFeatureGate`.
- `DreamTech.Leaderboard.League.Unity`: `PlayerPrefsLeagueTextStore` và `LeagueDebugPanel` — bảng thử IMGUI bày mọi thao tác
  của League, dùng được cả trong dev project lẫn trong game thật khi chưa có UI theo design.
- 59 test EditMode, trong đó `LeagueGroupServiceContract` là bộ hợp đồng mà MỌI bản cài `ILeagueGroupService` phải qua
  (viết backend mới thì tạo một lớp con của nó).
- **League hiển thị bằng chính leaderboard**: `LeagueBoardService` cài `ILeaderboardService` + `IScoreSource` trên một
  `LeagueSystem`, nên `LeaderboardBoard` / `LeaderboardWidget` chạy trên nhóm mùa này y như mọi bảng khác (ảo hoá, leo hạng,
  pill ▲N, banner top 3, row dính mép, skip). Điểm do dịch vụ giữ: `SubmitScoreAsync` chỉ đẩy hàng chờ cúp, bỏ qua con số
  client gửi. Các lượt hỏi gần như cùng lúc được gộp thành một lượt gọi mạng.
- `LeaderboardRowSkin` trong `LeaderboardThemeConfig` (hạng 1/2/3, row thường, row của mình): nền 9-slice, huy hiệu, bật/tắt
  số hạng, màu tên/điểm, nền điểm. Skin chỉ tính khi đã gán nền — theme cũ giữ nguyên cách tô màu.
- `LeaderboardScrollView.SetRankDividers` + `LeaderboardRankDivider`: chèn dải giữa các hạng (Promotion / Demotion...).
  `VirtualListLayout` có `ListDivider`; row đang leo lướt qua dải liên tục. Không có divider thì toán trùng khít bản cũ.
- `ILeaderboardRowDecorator`: component trên prefab row được gọi lại mỗi lần row bind (kể cả khi hạng đổi giữa animation) để
  game gắn nội dung riêng (rương theo hạng, cờ...). `LeaderboardEntryView` thêm slot tuỳ chọn `scoreBackgroundImage`.

### Changed
- `LeaderboardPrefabValidator`: `scoreBackgroundImage`, `avatarImage`, `avatarInitialText` là slot tuỳ chọn (row của game có
  avatar thật không cần avatar placeholder).

### Tests
- Tổng 171 test EditMode + 10 PlayMode (demo). Xanh trên Unity 6000.6.0f1 (171/171 EditMode, 10/10 PlayMode) và Unity 2022.3.62f2
  + TMP 3.0.7 (171/171 EditMode). Thêm: adapter League → `LeaderboardBoard` → `RankUp`, toán divider, smoke test scene demo League.

## [0.1.0] - 2026-09-11

Bản đầu. Viết lại từ package tham khảo `Wolffun.Leaderboard` v3 ("premium smooth"), giữ hướng thẩm mỹ và công thức
diễn, đổi toàn bộ tổ chức code sang dạng lắp ráp.

### Added
- Core thuần C# (`DreamTech.Leaderboard`): `LeaderboardEntry`, `RankTierRule`, `RankChange.Resolve`, `RevealSnapshot`,
  `BoardRowsBuilder`, `FetchWindowPlanner`, `LeaderboardBoard`, `LeaderboardBoardRegistry`.
- Bốn port: `ILeaderboardService`, `IScoreSource`, `ILeaderboardSnapshotStore`, `ILeaderboardFeedbackSink`
  (enum `LeaderboardBeat` chỉ thêm vào cuối).
- Adapter: `MockLeaderboardService` (tie-break ổn định, độ trễ giả lập, `ScoreToReachRank`, `FailNextCall`),
  `InMemoryLeaderboardSnapshotStore`, `PlayerPrefsLeaderboardSnapshotStore`, `ManualScoreSource`, `DelegateScoreSource`,
  `AudioSourceLeaderboardFeedback`, `UnityEventLeaderboardFeedback`.
- ViewModel thuần C# (`DreamTech.Leaderboard.ViewModel`): `RowState` giữ mọi trạng thái hiệu ứng, `BoardModel`,
  `RankUpPlanner`, `RevealTimeline` tick từ ngoài, `VirtualListLayout`, `BannerPlacement`, `SmoothDamping`, `DampedSpring`.
- UI (`DreamTech.Leaderboard.UI`): `LeaderboardWidget` với hợp đồng host `Arm → PresentAsync(hostReady) → Disarm`,
  trạng thái loading / lỗi + Retry / rỗng, `DebugTimeScale`; 5 config ScriptableObject.
- Editor: sinh art/âm tạm, tạo prefab mặc định, `LeaderboardPrefabValidator`. Menu sinh asset tự xám khi package chỉ đọc
  (cài bằng git URL).
- Hỗ trợ Unity 2022.3 (uGUI 1.0 + TMP 3.0.x hoặc TMP 3.2-pre) và Unity 6 (uGUI 2.x). Khác biệt `enableWordWrapping` /
  `textWrappingMode` tách bằng `versionDefines` (`DREAMTECH_LEADERBOARD_TMP_WRAPPING_MODE`).
- 99 test EditMode (75 Core + ViewModel, 24 UI), xanh trên Unity 6000.6.0f1 và 2022.3.62f2.
- Phát hành qua git URL: `https://github.com/TeamAcMong/unity-leaderboard.git#0.1.0`.

### Fixed (so với bản tham khảo)
- Skip giữa lúc leo làm mất pill "▲N" và shine: hiệu ứng giờ là dữ liệu trong `RowState`, view row mình được ghim,
  camera snap cùng frame.
- Banner top 3 / #1 che row người chơi: `BannerPlacement` đặt dưới row, hết chỗ thì lên trên.
- Row #1 bị cắt pill/glow ở mép trên: `topPadding` ≥ phần tràn, validator kiểm.
- Tên dài hiện bị cắt cụt thay vì "…": text tên bỏ Bold giả lập, đặt lại Ellipsis lúc chạy, validator + contract test.
- Sprite sinh ra bị cũ sau khi đổi thuật toán: generator luôn ghi đè và đóng dấu phiên bản.
- Pill trôi dần lên mỗi lần diễn: vị trí = gốc cố định + độ nâng theo tiến độ.
- Điểm bị ghi đè về giá trị cũ sau skip: điểm là một track của timeline, skip hoàn tất track.
- Exception giữa lúc diễn làm treo flow: tick bọc try/catch → `ForceFinish` → `PresentOutcome.Failed`.
- Confetti ra ô vuông trắng, ghi màu glow mỗi frame, sort hoà điểm không ổn định, tên có rich text đổi được giao diện,
  skip không tác dụng ở NewEntry/Unchanged, 4 lần tải tuần tự.
- Chữ trên avatar placeholder lấy ký tự đầu bất kể là gì (tên `<color=red>...` hiện "<", `_x` hiện "_"): giờ lấy chữ cái
  hoặc chữ số đầu tiên, duyệt theo text element.
