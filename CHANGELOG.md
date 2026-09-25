# Changelog

Mọi thay đổi đáng kể của package ghi ở đây. Định dạng theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
phiên bản theo [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-09-25

Bản phát hành đầu tiên có các thay đổi của **0.3.0** — 0.3.0 chưa từng được tag, toàn bộ mục 0.3.0 bên dưới nằm trong 0.4.0.

Nhóm mới: **nhịp lên hạng theo một game tham chiếu** (đường cong vẽ tay, tick đều khi leo, người bị vượt dồn xuống lúc đáp,
glow hiện/tắt dần, nhịp nhẹ khi có điểm mà không đổi hạng). Tất cả opt-in: cấu hình mặc định giữ nguyên hành vi 0.3.0, chốt bằng
dấu vân tay `RevealTimelineFlagOffTests` / `LeaderboardFlagOffRenderTests` (không ghi lại). Không đổi chữ ký public nào đang có; chỉ
thêm.

### Added
- `KeyframeCurve` (`ViewModel/Math`, thuần C#): tính đường cong keyframe giống hệt `UnityEngine.AnimationCurve` (Hermite + Bézier
  có trọng số) để tầng `noEngineReferences` chạy được curve vẽ tay. `LeaderboardMotionConfig.ToKeyframeCurve(AnimationCurve)`
  đổi từ curve của Unity.
- `MotionSettings.IntroSlideCurve` / `LiftCurve` / `ClimbCurve` / `LandCurve` (null = easing cũ) và bốn field `AnimationCurve`
  tương ứng trên `LeaderboardMotionConfig` (trống = như cũ). LandCurve thay cả OutBack lẫn cú đáp ba đoạn; giá trị ngoài [0, 1] là
  vọt / hụt.
- `MotionSettings.IntroRowDelayOffset`: độ trễ lệch đầu cộng vào mọi row của cú trượt vào.
- `BoardModel.StartIntro(topVisibleSlot, visibleRowCount)` + `BoardModel.IntroSettleSeconds`: số giây tới khi row cuối cùng THẤY
  ĐƯỢC đậu xong — host dùng để nối nhịp tiếp theo mà không phải tự đoán độ trễ lớn nhất. Overload một tham số giữ nguyên. Khi
  `HiddenLeadingSlots > 0` ô trên cùng không tính các ô sau bục.
- `MotionSettings.DeferPassSlidesToLand` + `LandPassSlideDuration` (0,12 s): người bị vượt đứng yên suốt cú leo rồi dồn xuống
  một lượt lúc row mình đáp.
- `MotionSettings.ClimbTickInterval`: nhịp `Pass` phát đều theo thời gian (mốc k × interval trong cú leo) thay vì theo từng người
  bị vượt. 0 = như cũ.
- `MotionSettings.GlowFadeInDuration` / `GlowFadeOutDelay` / `GlowFadeOutDuration` (0,5 s): glow của row mình hiện dần từ lúc
  nhấc, giữ suốt cú leo, tắt dần sau khi đáp. `RowState.StartGlowEnvelope` / `ReleaseGlowEnvelope` / `StopGlowEnvelope` /
  `IsGlowEnvelopeActive`. 0 = như cũ.
- `MotionSettings.QuietPulseInsteadOfBob`: lượt CÓ điểm mà không đổi hạng = nhấc + đáp (theo LiftCurve / LandCurve), không glow,
  không nhún sin; lượt 0 điểm (`Unchanged`) chốt trạng thái cuối ngay. Đi cùng `HostOwnsScoreCount` thì pha đếm điểm dài 0 (host
  đã đếm xong trong lúc giữ cổng).
- `RowState.TweenSlot(target, duration, linear)`; `LeaderboardScrollView.TryGetRowView(row, out view)` (view đang vẽ row, để host
  gắn hiệu ứng vào icon trong row).

### Changed (với cấu hình mặc định)
- `LeaderboardScrollView.SetModel(playIntro: true)` truyền `VisibleRowCapacity + 1` vào `StartIntro`. Chỉ ảnh hưởng
  `IntroSettleSeconds` (giá trị mới); quỹ đạo và nhịp không đổi.

### Tests
- `KeyframeCurveParityTests`: so `KeyframeCurve` với `AnimationCurve` trên các curve phẳng / dốc / có trọng số / nhiều khoá, sai
  số ≤ 1e-4.
- `RevealTimelineReferenceMotionTests`: curve điều khiển nhấc / leo / đáp; tick đều khi leo (kể cả frame dài); người bị vượt dồn xuống lúc
  đáp; glow hiện / tắt dần; nhịp nhẹ khi có điểm cùng hạng và chốt ngay khi 0 điểm; độ trễ lệch đầu của cú trượt vào;
  `IntroSettleSeconds`.
- Tổng 403 test EditMode (269 leaderboard + 134 League) + 13 PlayMode. Xanh trên Unity 6000.5.7f1 + uGUI 2.5.0 (403/403 EditMode,
  13/13 PlayMode; dev project để 6000.6.0f1 — bản chạy thử bỏ hai module chỉ có ở 6000.6) và Unity 2022.3.62f2 + uGUI 1.0.0 +
  TMP 3.0.7 (403/403 EditMode). Chạy thật trong Icon Match (2022.3.62f2, TMP 3.2.0-pre.12).

## [0.3.0] - 2026-09-23

_Chưa từng được tag riêng — phát hành trong 0.4.0._

Hai nhóm thay đổi, tất cả opt-in: **bàn giao nhịp cho host + chỉnh cú lên hạng theo clip tham chiếu** (cờ / tham số mới mà
mặc định giữ hành vi của 0.2.1, trừ hai điểm ghi ở "Changed") và **host tự trình bày các hạng đầu** (ví dụ bục ba cờ của
Golden Race): list không vẽ thanh cho hạng 1..K, màn lên hạng đáp vào đó dừng ở ranh giới chờ host diễn cú lên bục. Không đổi
chữ ký public nào đang có, enum chỉ thêm vào cuối.

Cờ bục tắt (`HostPresentedTopRanks` = 0) thì quỹ đạo, nhịp và hình trùng khít code **ngay trước khi thêm cờ bục** — tức đã
gồm nhóm thay đổi thứ nhất — chốt bằng dấu vân tay ghi từ code đó. Dấu vân tay KHÔNG so với 0.2.1.

### Added — bàn giao nhịp cho host (mặc định tắt)
- `MotionSettings.WaitForHostRelease` + `HostHoldTimeout` (8 s) + `LeaderboardWidget.ReleaseRevealHold()`: màn diễn đứng ở cuối
  Intro chờ host (ví dụ dòng phần thưởng rót xuống row); hết giờ thì tự đi tiếp và widget cảnh báo. Lời thả có hiệu lực ở tick kế
  tiếp bất kể độ dài frame; thả sớm không cắt ngắn `IntroWait`.
- `MotionSettings.HostOwnsScoreCount`: timeline không đụng vào điểm hiển thị của row mình (host tự đếm).
- `MotionSettings.RankFlipsBeforeRankMove`: lật số hạng + diễn nhịp "được lên hạng" ngay đầu màn, row vẫn bò lên như cũ.

### Added — cú lên hạng (mặc định = hành vi 0.2.1)
- Cú đáp ba đoạn `LandPeakScale` / `LandPeakAt` / `LandTroughScale` / `LandTroughAt` (0 = OutBack cũ); mốc trong pha đáp
  `LandFlashAt`, `LandTwinklesAt` (-1 = như cũ, lúc chạm đích); `FlashRiseDuration`, `FlashDecayPower`, `LandGlowFadesLate`;
  `RankUpPill`, `RankUpShine` (true = như cũ); `ClimbEasePower` (3 = InOutCubic cũ); `IntroSlideOvershoot`, `IntroFadeFraction`.
- `RowState.FlashNow(alpha, duration, riseDuration[, decayPower])`, `RowState.FlashLevel`; mốc dòng lên hạng
  `RowState.StartRankUpStream` / `EndRankUpStream` / `RankUpStreamStartTime` / `RankUpStreamEndTime` / `HasRankUpStream`;
  `Easing.InOutPower`.
- `LeaderboardRowRankUpStream`: mũi tên nổi trong row mình từ lúc nhấc tới lúc đáp. Bật bằng cách gắn component lên prefab row —
  prefab không có thì không vẽ gì.
- `LeaderboardVisualSettings.GlowFlashAlpha` / `GlowFlashPower` (glow sáng theo cú loé; 0 = tắt), `TwinkleMaximumDelay` (0,25 =
  như cũ), `TwinkleInsideFraction` (0 = chỉ viền, như cũ); `CelebrationBurstView.starSpriteVariants` (trống = một kiểu sao).

### Added — host tự trình bày các hạng đầu (mặc định tắt)
- `MotionSettings.HostPresentedTopRanks` (int, mặc định 0 = tắt): các row có `Rank < K` do host trình bày; vẫn nằm trong model với
  Slot như cũ (màn diễn cần chúng), chỉ phần vẽ được nhường cho host. Host đặt ô 0..K-1 sau bục bằng cách hạ `topPadding` đi
  K × (rowHeight + spacing).
- `MotionSettings.PodiumClimbDuration` (mặc định 0 = tới đích ngay frame được thả, không nhịp Pass / cuộn số) và
  `MotionSettings.PodiumPassSlideDuration` (mặc định 0 = đặt Slot thẳng, không tween) cho đoạn sau cổng bục. Cổng bục hết giờ theo
  `HostHoldTimeout` sẵn có.
- `BoardModel.HiddenLeadingSlots` (số row đầu bảng cuối, liên tiếp, không phải "...", có `Rank < K`, **không bao giờ quá K** — hạng
  bằng nhau thì row thứ K+1 ở lại list; có thể ít hơn K khi bảng tải thiếu / đứt quãng ngay dưới hạng 1, khi đó các ô từ đó tới K-1
  là row của list nằm trong dải chừa cho bục), `BoardModel.ListPresence(row)` (0 sau bục, 1 trong list, lẻ khi trượt ra khỏi bục —
  dùng chung cho list và host), `BoardModel.IsPresentedByHost(row)`, `BoardModel.LocalLandsOnPodium`.
- `RowState.IsHiddenFromList`: host giành một row khỏi list đúng frame proxy của nó cất cánh.
- `RevealPhase.PodiumHold`, `RevealPhase.PodiumClimb`; `LeaderboardBeat.PodiumTakeover` (phát đúng một lần khi row mình tới ranh giới,
  sau mọi nhịp Pass của đoạn trong list, trước Land). `RevealTimeline.TakesPodium`, `RevealTimeline.ReleasePodiumHold()` (có hiệu
  lực ở tick kế tiếp bất kể độ dài frame), `RevealTimeline.PodiumHoldTimedOut`. Kịch bản khi đáp vào bục: Lift → (Spin) → Climb tới
  ô ranh giới (bỏ qua nếu bắt đầu ở ranh giới / đã trên bục) → PodiumHold → PodiumClimb → Land như cũ. Bỏ qua / `ForceFinish` ở mọi
  pha coi như đã thả cổng; không đường nào treo.
- `LeaderboardWidget.ReleasePodiumHold()` (độc lập với `ReleaseRevealHold()`, gọi thừa / sớm vô hại) + cảnh báo khi cổng bục hết giờ.
- `LeaderboardScrollView.ListPresence(row)`, `GetRowAnchoredPosition(slot)`, `RenderContext`, `CreateRowProxy(row, parent)` /
  `DestroyRowProxy(proxy)` (bản sao thanh ngoài pool, vẽ đúng dáng list đang vẽ row đó — vị trí, cỡ `Scale × IntroScale`, bóng
  đổ — chỉ khác độ đục luôn đầy; huỷ nhầm view của list thì bỏ qua).

### Changed (với cấu hình mặc định)
- `FollowSmoothTime` ≤ 0 giờ là **bám neo** (camera nội suy theo slot giữa chỗ cuộn lúc bắt đầu và chỗ cuộn khi row về chỗ). Trước
  đây 0 bị kẹp lên 0,0001 và vẫn là bám mềm. Game đang để 0 sẽ thấy camera khác; muốn bám mềm thì đặt > 0 (mặc định 0,12 không đổi).
- Nhịp đáp: `Land` được phát TRƯỚC flash / shine / pill / sao (trước đây sau) — cùng một frame nên hình không khác, nhưng sink đọc
  `RowState` ngay ở nhịp `Land` sẽ thấy flash / pill chưa bật. Mọi màn lên hạng ghi mốc dòng lên hạng vào `RowState` (vô hại khi
  prefab row không có `LeaderboardRowRankUpStream`).

### Changed (chỉ khi `HostPresentedTopRanks` > 0)
- List: row có độ hiện diện 0 không có view (kể cả row mình đang ghim); độ hiện diện lẻ nhân vào độ đục.
- Camera "đỉnh là nhà" khi row mình đáp lên bục: bám neo (và bám mềm) về cuộn 0 đúng lúc chạm ranh giới; `SetModel`,
  `ScrollToLocalRow`, `RequestSnapToLocalRow` về 0 khi row mình đang sau bục / đã tới ranh giới.
- Không có thanh dính cho người chơi đang (hoặc sẽ) ở trên bục; không tia sáng cho row mình đang sau bục.
- Widget: đáp vào bục thì bỏ sao, tia sáng, banner, confetti neo theo thanh list (host ăn mừng trên cờ); nhịp Land / Celebrate vẫn
  phát.
- Planner: đáp lên bục thì số người diễn vượt được nới (có thể quá `AnimatedPasses`) cho tới ô ranh giới, để row mình luôn xuất
  phát trong list và bục "trước" đủ K người cũ — kể cả khi `AnimatedPasses` nhỏ hơn khoảng cách tới ranh giới hay dải hạng liền
  mạch bị cắt vì hạng bằng nhau.

### Fixed
- `PresentAsync` trên widget đang armed (không `Arm()` lại — Retry, trình bày lại sau quảng cáo) khi màn diễn trước đã xong và dữ
  liệu mới về chậm: Update kế tiếp kết thúc lượt MỚI bằng `RankChange` của màn diễn CŨ; màn diễn thật tới sau đó không bao giờ
  `PresentFinished`, skip catcher / bám camera / ghim row / chặn cuộn tay / reveal lease kẹt tới `Disarm`. Có từ 0.2.1.

### Tests
- `RevealTimelineFlagOffTests` (9): dấu vân tay từng tick (slot + hạng mọi row, cỡ / nhấc / glow / điểm / flash, mọi nhịp) của
  #10→#2, #3→#2, #6→#1, #48→#37 (+ bỏ qua giữa chừng) với cấu hình mặc định và kiểu Golden Race, ghi từ code ngay trước khi có cờ
  bục.
- `LeaderboardFlagOffRenderTests` (4): dấu vân tay từng frame của widget mẫu (cuộn, vị trí / cỡ / độ đục / thứ tự vẽ của view,
  banner) cho cùng các màn đó, ghi từ cùng code đó.
- `RevealTimelinePodiumTests` (63): cờ bật mà không đáp vào bục thì trùng từng bit với cờ tắt (kể cả đáp đúng ô ranh giới, quay số
  có đuôi, cấu hình kiểu host, bỏ qua); #10→#2 chờ ở ô 3 với người cũ hạng 3 còn ở ô 2, mọi nhịp Pass trước PodiumTakeover khi đoạn
  sau cổng 0 giây; bắt đầu ở ranh giới (#4→#3, #4→#1) và đổi chỗ trên bục (#3→#2, #2→#1, #3→#1) chờ ngay sau Lift; nhảy quay số
  (kể cả ít lượt diễn hơn khoảng cách tới ranh giới, và hạng bằng nhau cắt dải liền mạch) với bục "trước" đủ K người và row mình
  trong list; đoạn sau cổng 0 giây / có diễn; lời thả cổng bục và cổng Intro có hiệu lực ở tick kế tiếp ở 120 Hz, 90 Hz, 60 Hz dao
  động; thả sớm, hết giờ, bỏ qua ở mọi tick (kể cả host không bao giờ thả), `ForceFinish` lúc chờ, hai cổng độc lập; NEW / RankDown /
  Unchanged không có cổng bục; `HiddenLeadingSlots` với dải đứt, hạng bằng nhau (trần K), tải thiếu; `ListPresence`,
  `LocalLandsOnPodium`, `Clone`, mặc định tắt.
- `LeaderboardPodiumUITests` (21): không view cho row sau bục (kể cả row mình đang ghim), không rò / không phình pool, độ đục theo
  độ hiện diện, `IsHiddenFromList` không gây tạo lại view, camera về 0 lúc chờ (bám neo và bám mềm) và ở yên khi bắt đầu từ ranh
  giới / trên bục, cuộn / snap tới mình về đỉnh — các test camera chạy với dải trên list đủ lớn để canh giữa các ô bục KHÁC 0 (có
  kiểm tiền đề), không thanh dính (đối chứng cờ tắt có), không banner / tia sáng / sao khi đáp lên bục (đối chứng cờ tắt có), proxy
  tạo / huỷ không đụng pool, proxy lúc nhận quyền trùng cỡ và vị trí thanh đang nhấc, `TryGetRowBounds` cho row sau bục, cảnh báo
  hết giờ, bỏ qua lúc chờ, hình của màn lên hạng thường trùng khít khi bật cờ.
- `WidgetLifecycleTests.PresentAgainWhileArmed_DoesNotFinishTheNewRequestWithTheOldTimeline`: trình bày lại không `Arm()`, host
  sẵn sàng chậm một nhịp.
- `RevealTimelineHostHandoffTests`, `RevealTimelineLandCurveTests`, `RevealTimelineRankUpFeedbackTests`,
  `LeaderboardRowRankUpStreamTests`: cờ bàn giao cho host, cú đáp ba đoạn, nhịp "được lên hạng", dòng mũi tên.

## [0.2.1] - 2026-09-13

Sửa các lỗi League lộ ra khi chơi thử thật trong Icon Match quanh lúc đổi mùa: mất cúp, mùa bị lùi, kết quả mùa trùng làm
luồng popup lặp mãi. Không đổi chữ ký public nào đang có; chỉ thêm. **Dữ liệu mô phỏng đổi định dạng lưu, không tương thích
hai chiều với 0.2.0** — xem mục Changed.

### Fixed
- **Mất cúp thắng đầu mùa mới:** hết mùa (chưa gọi dịch vụ) rồi thắng → grant của mùa mới không khớp mùa dịch vụ đang giữ nên
  bị bỏ. `SimulatedLeagueGroupService.AddTrophiesAsync` giờ khép mùa cũ TRƯỚC rồi cộng vào mùa mới.
- **Mất các grant mùa cũ xếp hàng sau lúc đổi mùa:** grant đầu khép mùa, các grant sau bị bỏ. Mỗi mùa đã khép giờ có một sổ
  (cửa sổ mùa, bậc, cúp, id grant) nên grant tới muộn được cộng và kết quả được tính lại (hạng cuối, outcome, rương, bậc mùa sau
  nếu mùa sau chưa có cúp) khi người chơi chưa xem / chưa nhận.
- **Mở trang League lúc còn cúp chờ gửi khép mùa cũ thiếu cúp:** `LeagueBoardService` đẩy hàng chờ cúp (best-effort) trước khi
  hỏi bảng.
- **Đồng hồ lùi mở lại mùa đã khép:** trước đây mùa theo đồng hồ cũ hơn mùa đang giữ thì dịch vụ bỏ mùa đang giữ, mở lại mùa cũ
  0 cúp, khép lần hai sinh `SeasonResult` trùng `SeasonId`; xem / nhận trúng bản cũ nên thẻ kết quả + "New Season" lặp mãi,
  bậc bị cộng nhiều lần. Giờ mùa chỉ tiến: đồng hồ lùi thì giữ nguyên mùa đang giữ cùng số cúp, không bao giờ mở lại mùa đã
  khép, mỗi mùa tối đa một kết quả; `AcknowledgeResultAsync` / `ClaimSeasonRewardAsync` tác động đúng bản ghi đang chờ.
- **Mất cúp của mùa bị "nhảy qua":** thắng ở mùa N+1 nhưng mọi lần gửi trong N+1 thất bại (mất mạng, app bị tắt trong độ trễ
  ngay sau khi thắng) và lần gửi được đầu tiên rơi vào N+2 → grant không thuộc mùa đang giữ (N) cũng không phải mùa hiện tại nên
  bị từ chối `UnknownSeason`. Grant giờ mang cửa sổ mùa lúc thắng; dịch vụ dựng một mùa đã khép cho N+1 (bậc = bậc mùa sau của
  mùa liền trước), cộng cúp, tính kết quả riêng và đổi bậc mùa sau theo luật grant tới muộn — đúng cả khi dịch vụ còn giữ N lẫn
  khi một lượt gọi khác đã đưa nó sang N+2. Nhiều mùa trống bị nhảy qua thì chỉ mùa có grant có kết quả.
- **Cúp của mùa bị nhảy qua vẫn mất tuỳ thứ tự lượt gọi:** cùng một hàng chờ mà nhận hay bị từ chối `UnknownSeason` tuỳ lượt
  GetGroup ở mùa sau chạy trước hay sau lượt gửi — mùa đầu tiên dịch vụ từng giữ là mùa trống phía sau grant (lượt gửi đầu hỏng
  mà trang vẫn tải), hoặc giữa mùa của grant và mùa đang giữ còn một mùa trống đã mở rồi khép. `SimulatedLeagueGroupService` giờ
  nhận grant có cửa sổ mùa khi MỌI mùa phía sau nó đều trống (không cúp, không grant, kết quả chưa chốt): chèn sổ đúng thứ tự
  thời gian (bậc = bậc mùa sau của sổ khép liền trước, không có thì bậc mà mùa trống đầu tiên phía sau đã bắt đầu), tính lại
  các sổ trống phía sau theo chuỗi bậc mới (kết quả của chúng có thể xuất hiện / biến mất đúng luật) và bậc của mùa đang giữ nếu
  nó trống. Mùa phía sau đã có cúp hoặc kết quả đã chốt thì vẫn từ chối `UnknownSeason` (ghi rõ ở XML doc của
  `ILeagueGroupService`). Grant tới muộn cho mùa đã khép cũng truyền bậc qua các sổ trống phía sau tới mùa đang giữ còn trống
  (trước chỉ truyền khi đó là sổ khép gần nhất).
- **Token của một người gọi huỷ luôn lượt gửi cúp dùng chung:** `LeagueSystem.FlushPendingTrophiesAsync` gọi chồng nhau trả về
  cùng một Task chạy bằng token của người mở lượt; luồng kết quả mùa (token riêng) bị huỷ thì trang League đang nhập cùng lượt
  nhận huỷ không phải của mình, `LoadPageAsync` coi như lỗi mạng rồi hỏi bảng mùa mới khi cúp mùa cũ còn nằm hàng chờ → grant bị
  từ chối. Lượt gửi dùng chung giờ chạy bằng token riêng; mỗi người gọi chờ theo token của mình (huỷ thì chỉ người đó thôi chờ),
  lượt chỉ dừng khi mọi người chờ đều đã huỷ.
- **`LeagueBoardService` dùng lại lượt tải đang bay của mùa cũ cho mùa mới:** qua mốc đổi mùa trong lúc một nơi dùng khác còn
  tải bảng mùa cũ thì trang nhận entry mùa cũ với khoá mùa mới → lần mở sau RankDown / RankUp giả. Giờ chỉ nhập vào lượt đang bay
  của CÙNG mùa; lượt mùa cũ xong muộn không ghi đè snapshot mới hơn và không xoá lượt đang bay của mùa mới; `TryGetScore` trả
  false khi snapshot gần nhất thuộc mùa khác mùa hiện tại.
- **`LeaderboardWidget` đứng mãi ở "Loading" khi backend ném `OperationCanceledException` lạc:** mọi OCE đều bị coi là host
  huỷ → kết thúc `Cancelled`, không tắt Loading, không có Retry. Giờ chỉ coi là huỷ khi token của lượt trình bày đã huỷ; OCE khác
  đi nhánh lỗi (`ShowError` + Retry, kết quả `Failed`). Lượt bị lượt mới thay thế vẫn thoát êm.
- **League kẹt ở mùa tương lai sau cheat xoá dữ liệu:** một lượt gọi đang chờ độ trễ mang mùa đã tua tới, về sau khi reset thì mở
  mùa đó trên dữ liệu vừa xoá; mùa chỉ tiến nên League kẹt ở đó, mọi trận thắng bị từ chối, mở lại app vẫn vậy.
  `SimulatedLeagueGroupService` giờ từ chối mùa chưa bắt đầu theo đồng hồ của chính nó bằng `SimulatedLeagueException` (lỗi tạm,
  lần gọi sau đọc lại mùa), và lượt gọi bắt đầu trước `DebugResetSimulation()` thất bại bằng `SimulatedLeagueException` (lỗi
  tạm), không ghi gì. Không dùng `OperationCanceledException`: token của nơi gọi không bị huỷ, host lọc huỷ thành "người dùng huỷ"
  (widget, UniTask) sẽ im lặng đứng ở trạng thái đang tải.
- **Offset cheat tua giờ mất khi tắt/mở app** (gốc của lỗi đồng hồ lùi): `OffsetLeagueClock` có thêm bản lưu offset.
- **`LeaderboardBoard.MarkRevealed` ghi snapshot theo mùa lúc hạ cánh:** mùa đổi giữa lúc diễn thì lần mở sau ra RankUp /
  RankDown giả thay vì NEW. Snapshot giờ theo mùa của bảng đã tải.
- **Mốc đổi mùa rơi đúng lúc đang tải bảng:** `LeaderboardBoard.LoadSceneAsync` đọc khoá mùa sau khi sync nên entry thuộc mùa cũ
  mà khoá đã là mùa mới — cùng lỗi RankUp / RankDown giả ở lần mở sau. Khoá giờ đọc TRƯỚC lượt sync; khoá đổi trong lúc sync thì
  sync lại (tối đa 3 lượt) để entry và khoá cùng một mùa.
- **Nút "→ hết mùa" của `LeagueDebugPanel` không tới cuối mùa khi giờ máy chậm hơn mốc của đồng hồ không-lùi:** trước tua đúng
  "thời gian còn lại" (đếm từ mốc) nên vẫn kẹt trong mùa. Giờ tua tới `CurrentSeason.EndUtc` + 1 phút tính từ giờ của đồng hồ tua
  được; "Tua +1h / +6h" bù phần chậm để giờ League tiến đúng số giờ. Vòng tải lại của panel không còn bỏ yêu cầu tải lại khi một
  lượt gọi cũ bị dịch vụ bỏ do xoá dữ liệu (ghi một dòng "Lỗi tải" rồi chạy vòng đã xếp hàng); "Xem xong" / "Nhận rương" chỉ coi
  là huỷ khi panel bị huỷ, lỗi khác được ghi log thay vì lọt khỏi `async void`.
- **Trang / điểm của mùa mới cộng cả cúp còn nợ của mùa cũ:** `LeaguePageData.UnsentTrophies` và
  `LeagueBoardService.TryGetScore` chỉ tính cúp chờ gửi của đúng mùa đang hiện.
- **Kết quả mùa hiện ra thiếu cúp khi mất mạng:** `LeagueSystem.GetPendingSeasonResultAsync` trả null khi còn cúp của mùa khác
  chưa gửi được, tránh người chơi xem xong rồi cúp tới sau bị từ chối.
- **`LeagueBoardService`: HUD bị tắt làm popup đang tải cùng bảng hiện lỗi + Retry.** Lượt tải dùng chung chạy bằng token của
  người mở lượt: HUD mở lượt, popup nhập cùng lượt, token của HUD huỷ thì popup nhận `OperationCanceledException` không phải của
  mình. Lượt tải giờ chạy bằng token riêng như lượt gửi cúp (cả hai dùng chung một cơ chế nội bộ): mỗi người gọi chờ theo token
  của mình (huỷ thì chỉ người đó rời lượt, nhận huỷ của chính token đó); lượt chỉ dừng — huỷ lượt gọi dịch vụ đang bay, không ghi
  snapshot — khi mọi người chờ đều đã huỷ; lượt đang huỷ / đã xong không nhận thêm người, nên đóng popup rồi mở lại ngay thì mở
  lượt mới chứ không chờ phản hồi của lượt đã bỏ. Lượt mở trước (cùng mùa) xong muộn không ghi đè snapshot của lượt mở sau.
- **League chạy ngoài main thread sau khi gửi cúp → PlayerPrefs ném lỗi, mở trang League thất bại** (có từ 0.2.0):
  `LeagueBoardService` dùng `ConfigureAwait(false)`. Main thread của Unity có SynchronizationContext riêng nên .NET không chạy ngay
  phần tiếp theo đã bỏ context mà đẩy nó sang thread pool — kể cả khi lượt gửi cúp xong ngay trên main thread. Từ đó lượt hỏi bảng
  (và `Save` của `SimulatedLeagueGroupService` vào PlayerPrefs), việc đọc `ILeagueClock` / `ISeasonSchedule` của host, ghi snapshot
  và lưu mốc của `MonotonicLeagueClock` chạy trên thread pool. Gặp khi dịch vụ có độ trễ (mô phỏng có `LatencyMilliseconds`, backend
  thật) và còn cúp chờ gửi lúc mở trang / `SyncScoreAsync`. League giờ không dùng `ConfigureAwait(false)` ở đâu: phần sau mỗi
  await quay về context của nơi gọi, kể cả khi backend hoàn tất Task trên thread pool.
- **Mất kết quả + rương của mùa nằm trước một chuỗi mùa bị nhảy qua:** gửi cúp hỏng suốt nhiều mùa liền (mỗi mùa thắng một ván)
  rồi gửi bù được một lần — mỗi mùa bị nhảy qua dựng một sổ có kết quả chờ, số sổ còn chờ vượt
  `SimulatedLeagueOptions.MaximumStoredResults` và phần thu gọn bỏ luôn sổ cũ nhất: mùa trước chuỗi đó (đã có cúp, lên hạng, kết
  quả chưa xem) biến mất khỏi luồng kết quả, rương không bao giờ nhận được. `SimulatedLeagueGroupService` giờ không bao giờ bỏ sổ
  còn kết quả chờ khi thu gọn; số sổ được tạm vượt giới hạn và tự thu gọn sau khi xem / nhận.
- **`LeagueBoardService` dùng bảng trong cache mãi khi giờ máy chậm hơn mốc của `MonotonicLeagueClock`:** giờ League đứng yên ở mốc
  mà độ tươi của snapshot chỉ đo bằng giờ League, nên hiệu giờ = 0 mãi — mở trang bao nhiêu lần (cách nhau hàng giờ) cũng không gọi
  lại dịch vụ. Độ tươi giờ đo thêm bằng thời gian thực đơn điệu (`Stopwatch`, lưu mốc lúc ghi snapshot): snapshot chỉ tươi khi
  thời gian thực trôi chưa tới 0.5 giây VÀ giờ League trôi trong [0, 0.5 giây); `Invalidate` xoá cả hai mốc.
- **Kết quả mùa thiếu cúp hiện ra khi lượt hỏi kết quả vắt qua mốc đổi mùa:** `LeagueSystem.GetPendingSeasonResultAsync` chỉ kiểm
  cúp chờ gửi theo mùa đọc TRƯỚC lượt gọi dịch vụ. Yêu cầu gửi lúc 23:59 (cúp chờ gửi còn là của mùa hiện tại) được xử lý sau khi
  một lượt gọi khác (trang League lúc 00:01) đã khép mùa đó thiếu cúp → kết quả thiếu cúp được trả về, xem xong là chốt, các cúp
  tới sau bị từ chối `SeasonAlreadyFinalized`. Giờ kiểm lại sau lượt gọi: kết quả thuộc mùa còn cúp chờ gửi, hoặc thuộc đúng mùa
  đọc trước lượt gọi (mùa bị khép trong lúc gọi — với backend nhiều kết nối, phản hồi có thể mang bản tính trước khi cúp muộn tới dù
  hàng chờ đã trống), thì trả null, lần gọi sau thử lại.
- **`LeagueBoardService.SubmitScoreAsync` trả entry thiếu cúp vừa gửi:** gửi cúp xong nó nhập vào lượt tải cùng mùa đang bay — lượt
  đó có thể đã hỏi bảng trước khi grant tới dịch vụ (HUD đang tải, người chơi thắng, board thấy điểm lệch rồi gửi điểm) — nên nhận
  bảng cũ và còn phải chờ phản hồi của lượt đó. Giờ chỉ nhập lượt mở SAU lúc gửi xong; không có thì mở lượt mới.

### Added
- `MonotonicLeagueClock(inner, store, storeKey)`: giờ League không bao giờ lùi (`UtcNow` = max(giờ bọc trong, mốc cao nhất từng
  thấy)), lưu mốc có tiết chế (chỉ ghi khi mốc tiến thêm ít nhất 1 phút), `HighWaterUtc`, `IsInnerBehind`, `ResetHighWater()`.
- `OffsetLeagueClock(inner, store, storeKey)`: nạp offset đã lưu, đặt / tua thì ghi lại. Chuỗi hỏng thì bắt đầu từ 0.
- `LeagueTrophyGrantRejectedException` + `LeagueTrophyGrantRejection` (`SeasonAlreadyFinalized`, `UnknownSeason`): dịch vụ
  nhóm từ chối dứt khoát một grant (mùa đã chốt, mùa không nhận). Hợp đồng ghi ở XML doc của `ILeagueGroupService`.
- `LeagueSystem.TrophyGrantRejected` (sự kiện), `LeagueSystem.RejectedTrophyGrantCount`, `LeagueSystem.GetUnsentTrophies(seasonId)`:
  grant bị từ chối rời hàng chờ (không chặn grant sau) và được báo ra ngoài.
- `LeagueTrophyGrant(grantId, SeasonWindow season, trophies)` + `LeagueTrophyGrant.Season` (null khi tạo bằng constructor chỉ có
  id mùa). `LeagueSystem.RecordLevelWin` dùng constructor mới; hàng chờ cúp trên máy lưu thêm cửa sổ mùa (bản lưu cũ không có thì
  `Season` = null).
- `SimulatedLeagueGroupService.ActiveSeasonId`.
- `RankChange.SeasonKey` + `RankChange.Browse(entry, seasonKey)`.
- `LeagueDebugPanel.Create(league, simulation, clock, monotonicClock, featureGate, keepAcrossScenes)` và overload `Bind` tương
  ứng: nút "Xoá dữ liệu" xoá luôn mốc của đồng hồ không-lùi (tự nhận khi `league.Clock` là `MonotonicLeagueClock`); bảng hiện
  cảnh báo giờ máy chậm hơn mốc và số grant bị từ chối.
- `LeagueDebugPanel.AdvanceLeagueTime(duration)`: cho giờ League tiến đúng `duration` kể cả khi giờ máy + offset đang chậm hơn mốc.
- Contract test mới trong `LeagueGroupServiceContract` (mọi backend phải qua): cúp đầu mùa mới được tính, mọi grant mùa cũ
  xếp hàng đều tính, grant tới muộn tính lại kết quả chưa chốt / bị từ chối khi đã chốt, mỗi mùa một kết quả, xem + nhận
  idempotent theo mọi thứ tự, đồng hồ lùi không mở lại mùa đã khép, grant của mùa bị nhảy qua được tính cho mùa đó (khi còn giữ
  mùa trước, khi đã sang mùa sau, khi mùa đầu tiên dịch vụ giữ là mùa trống phía sau, khi có mùa trống đã khép nằm giữa) và bị từ
  chối `UnknownSeason` khi mùa phía sau đã có cúp.

### Changed
- `SimulatedLeagueOptions.MaximumStoredResults` giờ đếm số mùa đã khép giữ lại (kết quả + sổ cúp); mùa vừa khép gần nhất luôn
  được giữ. Chỉ sổ đã hết việc cho UI bị bỏ để giữ giới hạn: sổ còn kết quả chờ không bao giờ bị bỏ, nên số sổ có thể tạm vượt
  giới hạn.
- **Dữ liệu mô phỏng lưu định dạng 2; dữ liệu định dạng 1 của 0.2.0 bị bỏ** — `SimulatedLeagueGroupService` bắt đầu lại từ đầu
  (bậc khởi đầu, không mùa đang giữ, không kết quả / rương cũ), không ném lỗi. League chưa phát hành cho người chơi; save 0.2.0
  chỉ có trên máy dev/QA và có thể đã hỏng bởi lỗi đồng hồ lùi (mùa đang giữ ở tương lai, kết quả trùng mùa) theo cách không
  chuyển đổi an toàn được: giữ lại thì mọi trận thắng trong mùa thật bị từ chối cho tới khi giờ thật đuổi kịp.
- **Không hạ về 0.2.0 được:** 0.2.0 không đọc định dạng 2, sẽ bỏ dữ liệu mô phỏng đã lưu (bậc, mùa đang giữ, kết quả / rương chưa
  nhận) và ghi đè ở lần lưu kế tiếp.
- `LeagueSystem.FlushPendingTrophiesAsync` với token đã huỷ sẵn trả Task đã huỷ ngay (trước: hàng chờ rỗng thì trả 0).
- `LeagueBoardService.GetLocalEntryAsync` / `GetRangeAsync` với token đã huỷ sẵn trả Task đã huỷ ngay, không mở lượt tải, kể cả
  khi bảng trong cache còn tươi (trước: cache còn tươi thì trả bảng trong cache).
- `LeaderboardWidget`: `OperationCanceledException` không đến từ token của lượt trình bày giờ cho `PresentOutcome.Failed` (trước:
  `Cancelled`). Host nào dựa vào OCE lạc để im lặng thì sẽ thấy màn lỗi + Retry.

### Tests
- 244 test EditMode (110 leaderboard + 134 League; thêm 73: kịch bản playtest "đồng hồ lùi" và "mất cúp đầu mùa", grant của
  mùa bị nhảy qua ở mọi trạng thái — còn giữ mùa trước, đã sang mùa trống phía sau, mùa đầu tiên dịch vụ giữ là mùa trống phía
  sau, có mùa trống đã khép nằm giữa, mùa phía sau đã có cúp thì từ chối — kèm kết quả mùa trống xuất hiện / biến mất khi tính lại
  chuỗi bậc và đầu-cuối qua `LeagueSystem`; lượt gửi dùng chung bị một người gọi huỷ / mọi người gọi huỷ; lượt tải bảng dùng
  chung: người mở lượt huỷ mà board kia vẫn nhận bảng, một board huỷ rồi mở lại ngay nhận bảng từ lượt mới, mọi người chờ huỷ thì
  lượt gọi dịch vụ bị huỷ và lần sau tải lại bình thường; backend hoàn tất Task trên thread pool dưới một SynchronizationContext
  một luồng giống main thread — mọi lần đọc/ghi nơi lưu, đọc đồng hồ, gọi dịch vụ, `StateChanged` / `ScoreChanged` đều trên main
  thread; lượt tải đang bay của mùa cũ không dùng cho mùa mới; dữ liệu định dạng 1 → bắt đầu lại, lượt gọi mang mùa tương lai /
  đang bay lúc xoá dữ liệu (lỗi tạm, không phải huỷ); widget gặp `OperationCanceledException` lạc → `Failed` + Retry, token host
  huỷ → `Cancelled`; đồng hồ lưu offset / không-lùi / tiết chế ghi, snapshot theo mùa đã tải kể cả khi mốc đổi mùa rơi vào lúc
  tải; gửi bù sau chuỗi mùa gửi hỏng dài hơn giới hạn sổ vẫn giữ kết quả + rương của mùa lên hạng phía trước; lượt hỏi kết quả
  vắt qua mốc đổi mùa không trả kết quả thiếu cúp và cúp không bị từ chối, kể cả khi phản hồi về sau lượt gửi cúp muộn; `SubmitScoreAsync` không nhập lượt tải mở trước khi gửi
  cúp xong; giờ League đứng yên ở mốc của đồng hồ không-lùi mà snapshot vẫn hết hạn theo thời gian thực) + 13 PlayMode (demo + nút
  tua giờ và "Xoá dữ liệu" giữa lúc đang tải của `LeagueDebugPanel`). Xanh trên Unity 6000.6.0f1 (244/244 EditMode, 13/13
  PlayMode) và Unity 2022.3.62f2 + TMP 3.0.7 (244/244 EditMode).

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
