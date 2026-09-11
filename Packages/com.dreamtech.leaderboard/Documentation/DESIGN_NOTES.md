# Design notes — DreamTech Leaderboard

`CLAUDE.md` là luật ngắn. File này giải thích **vì sao** có các luật đó, để người sau không vô tình phá.

---

## 1. Yêu cầu gốc

- Game puzzle mobile cần leaderboard có game feel tốt, animation mượt, ngang tầm game trên thị trường.
  Tham chiếu chính: Royal Match, Match Villains.
- Làm trọn tính năng (dữ liệu, UI, animation), không chỉ hướng dẫn.
- **Lắp ráp kiểu Lego** (yêu cầu khi viết lại, 2026-09-11):
  - Điểm xếp hạng là wrapper cắm được nhiều cách tính, không buộc vào hệ thống nào của game.
  - Chỗ hiển thị tuỳ game: màn riêng, popup, hay một khối trong màn Win.
  - Module hoá, decoupling, cầm đi game khác vẫn lắp được.

---

## 2. Lịch sử

### Bản tham khảo `Wolffun.Leaderboard`

#### v1 — nền tảng (giữ lại)
Service interface → controller → list ảo hoá chạy theo `Slot` kiểu float → row view. Mock backend 3000 bot + adapter UGS.
Kịch bản lên hạng: hiện trạng thái cũ → nhấc row → leo, người bị vượt trượt xuống → hạ cánh với shine + confetti.
Nhảy lớn thì quay số rồi mới leo. Có sticky row "hạng của bạn".

#### v2 — "juicy" kiểu arcade (**BỊ TỪ CHỐI**)
Thêm vào v1:
- Anticipation lún xuống.
- Squash & stretch, nghiêng row theo vận tốc (spring).
- Ghost trail, tàn lửa khi leo.
- Người bị vượt bị xô ngang và flash tối.
- Va chạm húc người phía trên, hit-stop 60ms, rung panel.
- Vòng tia + confetti cho **mọi** lần lên hạng.
- Banner "RANK UP!" cho mọi lần lên hạng, whoosh.

Người dùng đánh giá: **"không hợp lắm"**, muốn đi từ v1 theo hướng smooth / polish như Royal Match.

**Bài học:** các kỹ thuật trên là ngôn ngữ của game đối kháng/arcade. Game puzzle casual cao cấp cần cảm giác gọn gàng và
tự tin, không ồn ào.

#### v3 — "premium smooth" (hướng hiện tại)
- **Chiều sâu** bằng bóng đổ mềm lớn dần khi nhấc row, không làm méo hình.
- **Nhường chỗ sớm:** người phía trên trượt xuống trước khi mình tới (`MakeRoomAt = 0.3`), ease-out không nảy.
- **Camera** `SmoothDamp` thay exp-lerp để vận tốc liên tục.
- **Intro:** row trồi lên theo trục Y và mờ dần vào, không trượt ngang.
- **Micro-polish:** số hạng cuộn kiểu odometer (giới hạn tần suất), badge nảy nhẹ khi vào tầng huy chương, điểm phồng nhẹ khi đếm.
- **Hạ cánh một nhịp:** bóng thu lại, OutBack 1.3, loé 0.35, shine, pill "▲N", vài ngôi sao, "ding", haptic vừa.
- **Thưởng theo tier:** sunburst, confetti, banner, fanfare chỉ cho top 3 và #1.
- **Visual "chunky":** sprite grayscale có gờ đáy tối và dải sáng mép trên, chữ trắng viền đậm qua TMP material, avatar khung trắng.

### DreamTech Leaderboard 0.1.0 (2026-09-11)

Viết lại v3 thành package lắp ráp. **Giữ nguyên** hướng thẩm mỹ, công thức vượt, cách dựng trạng thái cũ, cách nén cú nhảy
lớn. **Đổi** toàn bộ tổ chức code và sửa các lỗi thấy khi chạy thử bản tham khảo trong Editor (mục 5).

---

## 3. Research: phong cách Royal Match (tóm tắt, diễn giải)

- Animation nhanh và có kiểm soát; hiệu ứng ngắn, trôi chảy, vừa đủ thoả mãn.
  Nguồn: ironSource LevelUp, "Design Deep Dive #02 – Royal Match" (https://medium.com/ironsource-levelup/design-deep-dive-02-royal-match-948f7af96f04).
- Nhanh, mượt, dễ chịu; điểm mạnh là thực thi UI và animation hơn là ý tưởng mới.
  Nguồn: Deconstructor of Fun (https://www.deconstructoroffun.com/blog/2021/3/21/royal-match-the-new-king-from-turkey).
- **"Animated state transition"**: mở màn hình ở trạng thái cũ rồi animate sang trạng thái mới để củng cố cảm giác tiến bộ.
  Leaderboard này áp dụng đúng kỹ thuật đó.
  Nguồn: Funovus (https://www.funovus.com/blogs/royal-match-dominates-match-3-what-can-all-designers-learn/).
- King's Cup là giải theo thời gian, khoảng 50 người/nhóm: list ngắn, mỗi lần nhảy vài hạng, nên "vượt từng người" là
  trường hợp phổ biến.
- Ảnh UI tham khảo bố cục: Game UI Database (https://www.gameuidatabase.com/gameData.php?id=1061), mục Leaderboards.

**Chưa có:** số đo timing từng frame từ Royal Match hay Match Villains. Thông số hiện tại suy luận theo nguyên tắc. Khi có
video/ảnh chụp từng frame thì đo lại và sửa mục 4 — không đoán.

---

## 4. Thông số chính và lý do

| Tham số | Giá trị | Lý do |
|---|---|---|
| Tổng rank-up nhỏ | ~1.5s | "Nhanh, có kiểm soát"; không bắt người chơi chờ |
| `LiftDuration` | 0.22s | Đủ để mắt thấy row tách khỏi list trước khi di chuyển |
| `LiftScale` | 1.05 | Nổi bật mà không vỡ lưới |
| `MakeRoomAt` | 0.3 | Chồng lấn là nguồn "giật" lớn nhất; nhường chỗ sớm cho cảm giác trôi |
| `PassSlideDuration` | 0.26s, OutCubic | Trượt êm, không nảy (nảy ở row phụ gây nhiễu tiêu điểm) |
| `ClimbSecondsPerRow` | 0.09s, kẹp 0.5–1.4s | Leo xa vẫn dưới 1.5s |
| `MaxAnimatedPasses` | 15 | Quá 15 người thì quay số trước để giữ nhịp |
| `LandOvershoot` | 1.3 | "Đặt xuống" có trọng lượng, không bật nảy |
| `FollowSmoothTime` | 0.12s | Camera hơi trễ để thấy row tự đi lên, nhưng không tụt lại |
| Confetti | Chỉ top 3 / #1 | Phần thưởng phải có giá trị; dùng mọi lúc thì mất ý nghĩa |

---

## 5. Quyết định kiến trúc của bản viết lại

### 5.1 Vì sao tách ViewModel thuần C#
Mọi lỗi nặng của bản tham khảo nằm ở chỗ **trạng thái hiệu ứng sống trong view**: coroutine giữ tiến độ pill, view giữ
thời điểm shine. Skip hay pool dùng lại view là mất trạng thái. Ở bản này:

- `RowState` giữ **mọi** trạng thái hiệu ứng (vị trí slot, scale, lift, glow, flash, hạng/điểm hiển thị, pill, shine,
  cuộn số, nảy badge, lần đổi điểm cuối) cùng mốc thời gian theo đồng hồ của `BoardModel`.
- `LeaderboardEntryView.Render(RowState, clock)` là hàm thuần hình ảnh: hai view render cùng một state thì trông giống hệt nhau.
- `RevealTimeline` được widget tick từ ngoài, không coroutine. Test tua được từng tick, và test "skip ở mọi tick" chạy được.

### 5.2 Vì sao snapshot "lần xem cuối" thay cho cờ "đang có thay đổi chờ diễn"
Bản tham khảo giữ một "pending change" riêng, dễ lệch với backend (submit lỗi, đổi máy, đổi season). Bản này chỉ lưu
`RevealSnapshot(rank, score, seasonKey)` của lần diễn gần nhất. Mỗi lần mở, `RankChange.Resolve(snapshot, entry hiện tại)`
tính lại cần diễn gì từ dữ liệu thật. Không có trạng thái thứ hai nào để lệch.

`MarkRevealed` chỉ chạy khi timeline **chạm nhịp hạ cánh**. Đóng host trước đó thì lần sau diễn lại. Đóng sau đó thì
lần sau chỉ xem.

### 5.3 Vì sao board tự kéo điểm
Nếu nơi gọi truyền điểm (`Submit(levelsCompleted)`), mọi host phải biết chỉ số là gì và lấy ở đâu. Board kéo từ
`IScoreSource` nên host chỉ cần board id. Đổi chỉ số (level → sao → coin) = đổi một dòng ở composition root.

### 5.4 Vì sao hợp đồng host là `Arm → PresentAsync(hostReady) → Disarm`
- Host kiểu popup của MLGameKit **không bao giờ tắt GameObject** khi ẩn, nên `OnEnable` không phải tín hiệu "mở lần nữa".
- `Arm` phải chạy **trước** animation mở, nếu không lúc pop-in lộ row của lần trước trong vài frame.
- Tải dữ liệu song song với animation mở của host, nhưng intro chỉ bắt đầu khi `hostReady` xong (kẹp `HostReadyTimeout`),
  để intro không bị che bởi chính animation mở.
- `Disarm` giữ nguyên hình cho lúc host fade out, không xoá list giữa chừng.

### 5.5 Vì sao `LeaderboardBoardRegistry` chỉ là chỗ nối
Host (popup nằm trong Assembly-CSharp) và composition root có thể ở hai assembly khác nhau. Registry là static dictionary
theo board id, không phải service locator cho mọi thứ: widget vẫn nhận board qua request, không tự tra.

### 5.6 Vì sao sink nhận nhịp, không nhận clip
Âm thanh, haptic và analytics của mỗi game khác nhau. Timeline chỉ phát `LeaderboardBeat` + ngữ cảnh (tier, người thứ mấy
bị vượt, tiến độ quay số). Game tự quy ra clip, cao độ, độ rung. Widget bắt exception riêng từng sink để một sink hỏng không
làm hỏng màn diễn.

---

## 6. Lỗi của bản tham khảo → cách sửa → chốt chặn

Thấy khi chạy bản tham khảo trong Unity Editor ngày 2026-09-11.

| Lỗi | Nguyên nhân | Sửa | Chốt chặn |
|---|---|---|---|
| Skip giữa lúc leo làm mất pill "▲N" và shine | Hiệu ứng sống trong coroutine của view; skip nhảy camera, view row mình bị pool thu hồi | Hiệu ứng là dữ liệu trong `RowState`; ghim view row mình suốt lúc diễn; snap camera trong cùng `LateUpdate`, trước khi kiểm vùng hiển thị | `RevealTimelineTests.RankUp_SkipAtEveryTick`; `WidgetLifecycleTests.SkipMidClimb_LocalViewStillShowsPillAfterLanding`; ảnh chụp Play |
| Banner top 3 / #1 che row người chơi | Banner đặt cố định giữa list | `BannerPlacement.Resolve`: dưới row → trên row → kẹp phía rộng hơn | `BannerPlacementTests`; ảnh chụp Play |
| Row #1 bị cắt pill/glow ở mép trên | `topPadding` nhỏ hơn phần tràn (pill + glow + lift + bóng ≈ 47px) | `topPadding` 52 trên prefab | `LeaderboardPrefabValidator.ValidateWidget` |
| Tên dài bị cắt cụt thay vì "…" | TMP không tìm được `…` cho font/style đang dùng → tự đổi `overflowMode` sang `Truncate` và ghi vào component. Bold giả lập làm tăng rủi ro | Text tên không Bold; row view đặt lại Ellipsis + `richText=false` lúc khởi tạo; font tên được prewarm `…` | `ValidateRow` + contract test. **Chưa tái hiện được trong edit mode**, nguyên nhân suy từ source TMP 3.2 (`TextMeshProUGUI`, đoạn "Setup Ellipsis Special Character") |
| 2 sprite sinh ra bị cũ | Generator bỏ qua file đã có | Luôn ghi đè + đóng dấu phiên bản vào `userData` | `PlaceholderArtGenerator.IsCurrentVersion` |
| Sprite mới import bị ASTC 6x6 | `TextureImportProcessor` của MLGameKit không lọc đường dẫn | Generator áp lại override RGBA32 sau khi ghi | — |
| Pill trôi lên dần | Vị trí pill cộng dồn mỗi frame | Vị trí = gốc cố định + độ nâng theo tiến độ | `EntryViewRebindTests` |
| Điểm bị ghi đè về giá trị cũ sau skip | Coroutine đếm điểm vẫn chạy sau khi skip đặt điểm cuối | Điểm là một track của timeline, skip hoàn tất track | `RevealTimelineTests` |
| Exception giữa lúc diễn làm treo flow | Coroutine chết, không ai hoàn tất task | Tick bọc try/catch → `ForceFinish` → `Failed` | `WidgetLifecycleTests` |
| Confetti ra ô trắng | Sprite hạt không được nối | Sprite nối trong prefab | Contract prefab |
| Ghi màu glow mỗi frame | Không so giá trị cũ | Lượng tử hoá alpha (`GlowAlphaSteps`), chỉ ghi khi đổi | — |
| Hoà điểm nhảy thứ tự giữa các lần tải | Comparator thiếu khoá phụ | Mock xếp theo thứ tự đạt điểm; builder sort hạng → điểm → id | `MockLeaderboardServiceTests` |
| Tên `<color=red>` đổi được giao diện | `richText` bật | `richText = false` | Contract prefab; ảnh chụp Play |
| Skip không tác dụng ở NewEntry / Unchanged | Skip chỉ nối vào nhánh RankUp | Timeline skip mọi kind | `RevealTimelineTests` |
| 4 lần tải tuần tự | `await` nối nhau | `Task.WhenAll` cho top + window | — |
| Avatar của tên `<color=red>...` hiện "<" | Lấy text element đầu tiên bất kể là gì (thấy khi chụp Play, bản mới cũng dính) | Lấy chữ cái / chữ số đầu tiên | `EntryViewInitialLetterTests` |

---

## 7. Đã kiểm chứng và chưa

**Đã kiểm chứng (2026-09-11, Unity 2022.3.62f2, Icon Match):**
- 106 test EditMode xanh: 75 Core + ViewModel, 24 UI, 7 glue Icon Match.
- Play từ `Boot.unity`, gọi cheat, chụp Game View mỗi 0.1s:

| Kịch bản | Kết quả |
|---|---|
| Hạng 121 → leo 12 | `Completed RankUp 120→108`, hạng liền mạch, pill ▲12 + shine ở đúng row |
| Như trên, skip giữa lúc leo | `Skipped`, camera snap ngay, pill ▲12 + shine vẫn diễn sau khi hạ cánh |
| Hạng 12 → top 3 | Banner "TOP 3!" nằm dưới row, confetti + sunburst |
| Hạng 10 → #1 | Banner "YOU'RE #1!" dưới row; pill + glow của row #1 không bị cắt |
| Người chơi mới | Row trồi vào chỗ trống, pill NEW, điểm đếm lên |
| Điểm tăng, hạng giữ | Pill BEST |
| Không đổi | Nhún nhẹ, `Completed Unchanged` |
| Đóng trước hạ cánh rồi mở lại | Lần 1 `Cancelled`, snapshot chưa ghi → lần 2 diễn lại `RankUp` |
| Đóng sau hạ cánh rồi mở lại | `Unchanged`, không diễn lại |
| Mở popup nhiều lần | Lúc pop-in không lộ row cũ |
| `FailNextCall` | Thông báo lỗi + Retry; bấm Retry tải lại được |
| Tên dài / tiếng Việt / `<color=red>Hacker</color>` | "Nguyễn Thị Ánh T…", đủ dấu, một font; tên có thẻ hiện nguyên văn "<color=red>Hack…" |
| Console | Không error, không cảnh báo Ellipsis của TMP |

**Ma trận phiên bản (2026-09-11, chạy batchmode):**

| Unity | uGUI / TMP | Kết quả |
|---|---|---|
| 6000.6.0f1 (dev project) | uGUI 2.6.0, `com.unity.textmeshpro` resolve thành shim 5.0.0 | Compile 0 warning; 99/99 EditMode; 5/5 PlayMode chạy scene demo (3 host, skip, đóng trước hạ cánh, #1 + người chơi mới, lỗi backend) |
| 2022.3.62f2 (project tạm) | uGUI 1.0.0 + TMP 3.0.7 (khai báo 3.0.6, Unity tự nâng) | Compile 0 warning; 99/99 EditMode |
| 2022.3.62f2 (Icon Match) | TMP 3.2.0-pre.12 | 99/99 EditMode + 7 test glue + chạy thật ở trên |

Mọi GUID mà prefab của package tham chiếu (font `LiberationSans SDF`, shader `TMP_SDF-Mobile`, script uGUI/TMP) đều resolve
ở cả hai đời Unity: TMP Essential Resources giữ nguyên GUID giữa TMP 3.0 và uGUI 2.x.

**Chưa kiểm chứng:**
- Haptic trên máy iOS/Android thật.
- Backend thật (chưa có adapter nào ngoài Mock).
- SFX thật (đang dùng WAV tổng hợp tạm).
- Nhiều tỉ lệ màn hình (mới chụp ở 1080x1920).

---

## 8. Backlog (chưa làm, cần người dùng đồng ý)

- Tự submit khi thắng level (với MLGameKit: `WinHandler.OnWin` chạy **trước** khi `CurrentLevel` tăng → phải đợi frame sau).
- Tự diễn khi về Home, icon leaderboard ở Home, khối leaderboard trong màn Win.
- Adapter backend thật (UGS hoặc server riêng) — asmdef riêng + `versionDefines`.
- Rank tụt: hiện "▼N" hay im lặng.
- League/nhóm có vùng lên/xuống hạng (Royal League), tab Weekly / All-time / Friends.
- Avatar thật (tải ảnh, cache) thay chữ cái đầu.
- Profile popup khi chạm một row.
- CI chạy ma trận test (Unity 6 + 2022.3) cho mỗi PR.

Đã xong: tách thành repo riêng `TeamAcMong/unity-leaderboard` có dev project Unity 6 + tag, cài qua git URL (như UI Core và
Addressable System).
