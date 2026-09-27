using System;

namespace DreamTech.Leaderboard.ViewModel
{
    /// <summary>
    /// Mọi tham số thời gian/chuyển động mà view-model dùng. Widget chụp một bản sao lúc bắt đầu diễn, nên chỉnh config
    /// giữa chừng (inspector, remote config) không làm lệch một màn đang chạy.
    ///
    /// <para>Mặc định giữ đúng số của bản tham khảo "premium smooth": animation ngắn, mượt, một tiêu điểm là row người chơi.</para>
    /// <para>[Serializable] để config ScriptableObject nhúng thẳng vào inspector; bản thân class vẫn thuần C#.</para>
    /// </summary>
    [Serializable]
    public sealed class MotionSettings
    {
        // ---------------------------------------------------------------- Màn diễn
        public float IntroWait = 0.3f;
        public float LiftDuration = 0.22f;
        public float LiftScale = 1.05f;
        public float ClimbSecondsPerRow = 0.09f;
        public float ClimbDurationMinimum = 0.5f;
        public float ClimbDurationMaximum = 1.4f;
        public float SpinDuration = 0.7f;
        public float PassSlideDuration = 0.26f;

        /// <summary>Người phía trên nhường chỗ khi row mình đi được bao nhiêu phần ô. Nhỏ = nhường sớm, mượt hơn.</summary>
        public float MakeRoomAt = 0.3f;

        /// <summary>
        /// Độ "dồn" của cú leo: 3 (mặc định) = InOutCubic như cũ, 2 = InOutQuad (êm hơn ở hai đầu, tốc độ đỉnh chỉ gấp đôi
        /// trung bình thay vì gấp ba). Giá trị khác 3 dùng <c>Easing.InOutPower</c>.
        /// </summary>
        public float ClimbEasePower = 3f;

        public float LandDuration = 0.32f;

        /// <summary>
        /// Cỡ ĐỈNH của cú đáp (tuyệt đối, ví dụ 1,134). 0 (mặc định) = tắt: pha đáp dùng đường OutBack cũ từ cỡ lúc đáp về 1.
        ///
        /// <para>Bật lên thì pha đáp đi ba đoạn: vọt lên <see cref="LandPeakScale"/> (OutQuad) → hụt xuống
        /// <see cref="LandTroughScale"/> (InQuad) → về 1 (OutQuad). OutBack không vẽ được hình này: nó chỉ đi MỘT chiều
        /// từ cỡ lúc đáp về 1 rồi vượt quá — không bao giờ vọt lên trước. Mà "vọt lên rồi hụt xuống" chính là cú
        /// "thịch" của một vật vừa đặt xuống.</para>
        /// </summary>
        public float LandPeakScale;

        /// <summary>Phần của <see cref="LandDuration"/> lúc chạm đỉnh (0..1).</summary>
        public float LandPeakAt = 0.31f;

        /// <summary>Cỡ ĐÁY của cú đáp (tuyệt đối, ví dụ 0,958). Chỉ dùng khi <see cref="LandPeakScale"/> &gt; 0.</summary>
        public float LandTroughScale = 1f;

        /// <summary>Phần của <see cref="LandDuration"/> lúc chạm đáy (0..1, phải &gt; <see cref="LandPeakAt"/>).</summary>
        public float LandTroughAt = 0.79f;

        /// <summary>
        /// Flash của row mình nổ ở phần bao nhiêu của pha ĐÁP (0..1). -1 (mặc định) = nổ cùng nhịp "đáp" như cũ (pill, sao,
        /// shine) — mà khi <see cref="RankFlipsBeforeRankMove"/> bật thì nhịp đó chạy ở cú LẬT, tức flash nổ lúc số vừa đổi.
        ///
        /// <para>Đặt ≥ 0 để tách flash khỏi cú lật và cho nó nổ trong pha đáp — đúng chỗ một cú "bừng sáng" thuộc về:
        /// lúc row vừa chạm chỗ mới, chứ không phải lúc nó còn chưa kịp đi.</para>
        /// </summary>
        public float LandFlashAt = -1f;

        /// <summary>
        /// Flash cần bao nhiêu giây để LÊN tới đỉnh (OutQuad), rồi mới tắt dần trong <see cref="FlashDuration"/>.
        /// 0 (mặc định) = bật ở đỉnh ngay tức thì, như cũ.
        ///
        /// <para>Một cú loé bật tức thì đọc ra "chớp đèn"; một cú loé có sườn lên ngắn đọc ra "bừng sáng". Clip tham
        /// chiếu là loại thứ hai: độ sáng hàng lên đỉnh trong ~0,12 s rồi mới tắt dần ~0,47 s.</para>
        /// </summary>
        public float FlashRiseDuration;

        /// <summary>
        /// Hình của sườn TẮT: độ loé = đỉnh × (1 − p)^FlashDecayPower. 1 (mặc định) = tắt đều, như cũ.
        ///
        /// <para>Lớn hơn 1 khi project dựng hình ở Linear color space mà muốn cú tắt TRÔNG đều như trên một game
        /// gamma: cộng sáng trong không gian tuyến tính rồi mới đổi sang sRGB làm đoạn đầu của cú tắt trông như
        /// đứng yên. Clip tham chiếu (gamma) tắt đều trong 0,45 s; ở Icon Match (Linear) 1,4 cho cùng đường cong
        /// trên màn hình, sai lệch ≤ 0,03.</para>
        /// </summary>
        public float FlashDecayPower = 1f;

        /// <summary>
        /// Sao lấp lánh quanh row mình (<c>IRevealListener.OnLanded</c>) bung ở phần bao nhiêu của pha ĐÁP (0..1).
        /// -1 (mặc định) = đi cùng nhịp "đáp" như cũ — mà khi <see cref="RankFlipsBeforeRankMove"/> bật thì nhịp
        /// đó chạy ở cú LẬT.
        ///
        /// <para>Tách riêng khỏi <see cref="LandFlashAt"/> vì sao có độ trễ nở riêng của nó: muốn sao và flash
        /// cùng lên đỉnh thì sao phải bung SỚM hơn flash một chút.</para>
        /// </summary>
        public float LandTwinklesAt = -1f;

        /// <summary>
        /// Có hiện pill "▲N" (số người vừa vượt) khi lên hạng không. true (mặc định) = như cũ.
        ///
        /// <para>Tắt khi game đã có chỉ báo lên hạng riêng (ví dụ dòng mũi tên nổi trong row —
        /// <c>LeaderboardRowRankUpStream</c>); hai chỉ báo cùng lúc là nói một điều hai lần.</para>
        /// </summary>
        public bool RankUpPill = true;

        /// <summary>Có quét vệt shine ngang row khi lên hạng không. true (mặc định) = như cũ.</summary>
        public bool RankUpShine = true;

        /// <summary>
        /// Có hiện pill "BEST" khi điểm tăng mà hạng đứng yên (<c>RankChangeKind.ScoreImproved</c>) không. true (mặc định) =
        /// như cũ.
        ///
        /// <para>Tắt khi game coi lượt "có điểm, không đổi hạng" là một nhịp nhẹ im lặng (<see cref="QuietPulseInsteadOfBob"/>):
        /// một viên thuốc xanh đứng 1,7 s trên hàng nói to hơn chính nhịp đó.</para>
        /// </summary>
        public bool ScoreImprovedPill = true;

        /// <summary>
        /// Row mình MỚI vào bảng (<c>RankChangeKind.NewEntry</c>) có kèm pill "NEW", vệt shine và cú loé không. true (mặc định)
        /// = như cũ. Tắt thì cú nở (Pop) vẫn chạy, nhịp <c>NewEntry</c> vẫn phát cho sink, chỉ bỏ ba điểm nhấn đó.
        /// </summary>
        public bool NewEntryAccent = true;

        /// <summary>
        /// Glow của row mình giữ sáng suốt cú đáp rồi mới tắt ở cuối (InCubic), thay vì tắt ngay từ đầu pha đáp
        /// (OutCubic — mặc định, như cũ).
        ///
        /// <para>Bật khi flash nổ GIỮA pha đáp (<see cref="LandFlashAt"/> ≥ 0) và glow đi theo flash
        /// (<c>GlowFlashAlpha</c>): tắt glow sớm là có một nhịp tối giữa lúc row chạm đích và lúc flash lên —
        /// clip tham chiếu không có nhịp tối đó, viền sáng liền mạch từ lúc leo sang cú bừng sáng.</para>
        /// </summary>
        public bool LandGlowFadesLate;
        public float LandOvershoot = 1.3f;
        public float LandFlashAlpha = 0.35f;
        public float FlashDuration = 0.3f;
        public float ScoreCountDuration = 0.6f;

        /// <summary>
        /// Host tự chạy bộ đếm điểm của row mình, timeline KHÔNG đụng vào.
        ///
        /// <para>Mặc định <c>false</c> — timeline nội suy <c>FromScore → ToScore</c> trong
        /// <see cref="ScoreCountDuration"/> giây. Đó là hành vi đúng cho phần lớn game: không ai phải làm gì
        /// mà con số vẫn chạy lên.</para>
        ///
        /// <para>Bật lên khi host có một hiệu ứng BAY (đồng xu, token, rương) rót từ một nguồn nào đó xuống
        /// row mình, và muốn con số nhảy ĐÚNG lúc từng vật đáp. Hai bên cùng ghi một con số thì chúng cãi
        /// nhau từng frame: timeline nội suy mượt trong khi host nhảy từng nấc, và kết quả là một bộ đếm giật
        /// ngược — tệ hơn hẳn so với chọn hẳn một bên.</para>
        ///
        /// <para>Timeline VẪN chốt giá trị cuối ở lúc đáp và lúc kết thúc (kể cả khi bị bỏ qua), nên host có
        /// chết giữa chừng thì con số vẫn về đúng <c>ToScore</c> — không bao giờ nuốt điểm của người chơi.</para>
        /// </summary>
        public bool HostOwnsScoreCount;

        /// <summary>
        /// Số hạng của row mình đổi NGAY khi màn diễn bắt đầu, thay vì lúc đáp.
        ///
        /// <para>Mặc định <c>false</c> = ẩn dụ "leo": số hạng cũ giữ nguyên trong lúc row bò lên, và chỉ đổi
        /// khi tới nơi. Bật lên = ẩn dụ "bảng xếp lại": số hạng đổi tức thì, RỒI danh sách mới cuộn cho hàng
        /// xóm khớp với hạng mới.</para>
        ///
        /// <para>Đây là lựa chọn DIỄN XUẤT, không phải tối ưu: ẩn dụ thứ hai giữ mắt người chơi ở nguyên một
        /// chỗ (con số của họ), còn ẩn dụ thứ nhất bắt mắt đuổi theo row đang chạy. Game nào có row mình làm
        /// tiêu điểm cố định thì chọn cái thứ hai.</para>
        /// </summary>
        public bool RankFlipsBeforeRankMove;

        /// <summary>
        /// Timeline dừng ở cuối pha Intro cho tới khi host gọi <c>RevealTimeline.ReleaseHostHold()</c>.
        ///
        /// <para>Mặc định <c>false</c> — màn diễn tự chạy hết, không phụ thuộc ai.</para>
        ///
        /// <para>Bật lên khi host có một nhịp riêng phải diễn XONG trước khi bảng được phép xếp lại (ví dụ
        /// một dòng phần thưởng rót vào row mình). Cách thay thế duy nhất là nhét thời lượng nhịp đó vào
        /// <see cref="IntroWait"/> — tức chép một con số thuộc về hệ khác, và nó sai ngay lần đầu nhịp kia
        /// đổi độ dài, hoặc khi nhịp kia DÀI NGẮN KHÁC NHAU tuỳ lượt (có/không có phần thưởng). Đợi tín hiệu
        /// thì không có con số nào để lệch.</para>
        ///
        /// <para>Kèm <see cref="HostHoldTimeout"/> làm lưới vớt: một host quên gọi không được phép treo màn
        /// diễn vĩnh viễn.</para>
        /// </summary>
        public bool WaitForHostRelease;

        /// <summary>
        /// Trần số giây chờ host, tính từ lúc pha Intro kết thúc. Quá hạn thì timeline tự đi tiếp và ghi một
        /// cảnh báo — thà lệch nhịp còn hơn một màn hình đứng hình không có đường thoát.
        ///
        /// <para>Cũng là trần của cổng bục (pha <c>PodiumHold</c>, xem <see cref="HostPresentedTopRanks"/>), tính từ lúc
        /// row mình tới ranh giới. Dùng chung một con số vì cả hai cùng trả lời một câu: "host được phép im lặng bao lâu
        /// trước khi coi như nó đã chết".</para>
        /// </summary>
        public float HostHoldTimeout = 8f;

        /// <summary>
        /// Số hạng đầu (0-based: mọi row có <c>Rank &lt; K</c>) do HOST tự trình bày — ví dụ một bục vinh quang ba cờ nằm
        /// trên đầu list — nên list KHÔNG vẽ thanh cho chúng. 0 (mặc định) = tắt: list vẽ mọi row, màn diễn y như cũ.
        ///
        /// <para>Các row đó VẪN ở trong model với Slot như cũ (ô 0..K-1): màn diễn cần chúng để dựng trạng thái cũ, tính
        /// người bị vượt và chỗ đáp. Chỉ phần VẼ được nhường cho host. <c>BoardModel.ListPresence</c> là độ hiện diện trên
        /// list (0 = sau bục, 1 = trong list, lẻ = đang trượt ra khỏi bục) và được dùng chung cho list lẫn host, để chỗ
        /// giao nhau giữa hai bên khớp nhau từng frame. Host muốn các ô 0..K-1 nằm sau bục thì hạ <c>topPadding</c> của
        /// list đi K × (rowHeight + spacing).</para>
        ///
        /// <para>Số ô thật sự giấu là <c>BoardModel.HiddenLeadingSlots</c>: không bao giờ quá K (hạng bằng nhau thì row thứ K+1
        /// ở lại list), nhưng có thể ÍT hơn K khi bảng tải thiếu hạng đầu hoặc đứt quãng ngay dưới hạng 1 — khi đó các ô
        /// <c>HiddenLeadingSlots</c>..K-1 là row của list và rơi vào dải chừa cho bục. Bảng tải đủ hạng đầu (TopCount ≥ K, hạng
        /// là thứ tự xếp) thì luôn đúng K.</para>
        ///
        /// <para>Bật lên thì màn lên hạng ĐÁP vào phần đó đổi kịch bản: row leo trong list tới ranh giới (ô K), dừng ở pha
        /// <c>PodiumHold</c> và phát <c>LeaderboardBeat.PodiumTakeover</c>; host diễn cú lên bục rồi gọi
        /// <c>ReleasePodiumHold()</c> (quá <see cref="HostHoldTimeout"/> thì tự đi tiếp); pha <c>PodiumClimb</c> đưa row
        /// tới đích trong <see cref="PodiumClimbDuration"/>, rồi Land như cũ. Mọi màn lên hạng KHÔNG đáp vào phần đó (ví dụ
        /// 48 → 37) diễn y hệt khi cờ tắt — chúng đã được chỉnh từng khung.</para>
        /// </summary>
        public int HostPresentedTopRanks;

        /// <summary>
        /// Sau khi host thả cổng bục, row mình (lúc này đã nằm sau bục, list không vẽ) đi nốt từ chỗ đứng chờ tới ô đích
        /// trong bao nhiêu giây. 0 (mặc định) = tới đích ngay trong frame được thả.
        ///
        /// <para>Mặc định 0 vì host vừa diễn cú lên bục bằng hình của chính nó: lúc thả cổng hình đã ở trạng thái cuối, model
        /// chỉ cần bắt kịp. Tới đích ngay thì những người bị vượt ở đoạn này cũng không "diễn": không nhịp Pass, không cuộn
        /// số. Đặt &gt; 0 khi host muốn vẽ THEO <c>RowState.Slot</c> trong đoạn này thay vì tự diễn.</para>
        /// </summary>
        public float PodiumClimbDuration;

        /// <summary>
        /// Thời gian trượt của những row bị vượt trong pha <c>PodiumClimb</c> (thay cho <see cref="PassSlideDuration"/>).
        /// 0 (mặc định) = đặt Slot thẳng, không tween.
        ///
        /// <para>Mặc định 0 vì host thường đã hạ người vừa mất hạng 3 xuống chỗ thanh hạng 4 bằng proxy của nó: model phải
        /// đặt row đó vào đúng chỗ ấy trong CÙNG frame được thả để host huỷ proxy mà không có frame nào hở hay trùng hình.</para>
        /// </summary>
        public float PodiumPassSlideDuration;
        public float NewEntryPopDuration = 0.4f;
        public float NewEntryPopOvershoot = 1.6f;
        public float BobDuration = 0.36f;
        public float BobAmplitude = 0.035f;
        public float MinimumPassBeatInterval = 0.05f;
        public float MinimumSpinBeatInterval = 0.05f;

        // ---------------------------------------------------------------- Intro của list
        public float IntroStagger = 0.03f;
        public float IntroDuration = 0.32f;
        /// <summary>Row lùi bao nhiêu theo trục DỌC lúc intro (dương = lùi xuống dưới rồi trồi lên).</summary>
        public float IntroOffset = 36f;

        /// <summary>
        /// Row lùi bao nhiêu theo trục NGANG lúc intro. Dương = bắt đầu ở bên PHẢI rồi trượt sang trái vào
        /// chỗ; âm = từ bên trái. 0 (mặc định) giữ nguyên hành vi cũ — intro chỉ đi theo trục dọc.
        ///
        /// <para>Tách riêng khỏi <see cref="IntroOffset"/> thay vì đổi nó thành Vector2: field float đã nằm
        /// trong mọi asset MotionConfig đang chạy, đổi kiểu là mọi bản cấu hình cũ mất giá trị mà không có
        /// một dòng cảnh báo nào.</para>
        ///
        /// <para>Dùng chung một đường cong với trục dọc (cùng <c>IntroDuration</c>, cùng OutCubic): hai trục
        /// chạy hai nhịp khác nhau thì row đi theo đường cong chữ S, không ai muốn thế.</para>
        /// </summary>
        public float IntroOffsetX;
        public float MaximumIntroDelay = 0.36f;

        /// <summary>
        /// Độ VƯỢT của cú trượt vào (tham số <c>Easing.OutBack</c>). 0 (mặc định) = giữ đường cong cũ
        /// <c>OutCubic</c>, không vượt.
        ///
        /// <para>Lớn hơn 0 thì row trượt quá chỗ đậu một chút rồi bật về — thứ làm list đọc ra "đáp xuống"
        /// thay vì "dừng lại". Chỉ áp cho ĐỘ LÙI (<see cref="IntroOffset"/>, <see cref="IntroOffsetX"/>), không áp
        /// cho độ đục và cỡ: một row mờ quá 100% hay phình theo đúng nhịp trượt là hai hiệu ứng không ai
        /// muốn đi kèm.</para>
        ///
        /// <para>Tách field riêng thay vì dùng lại <see cref="IntroOvershoot"/>: field đó đang lái CỠ của row
        /// trong mọi asset hiện có, đổi nghĩa nó là đổi hình của mọi game đang chạy.</para>
        /// </summary>
        public float IntroSlideOvershoot;

        /// <summary>
        /// Row đạt độ đục đầy ở phần bao nhiêu của <see cref="IntroDuration"/> (0..1). 1 (mặc định) = độ đục đi
        /// cùng nhịp với cú trượt, như cũ.
        ///
        /// <para>Nhỏ hơn 1 khi row trượt vào từ NGOÀI màn hình: lúc nó lộ ra ở mép thì phải đã đặc rồi. Để độ
        /// đục chạy chung nhịp thì phần row nhìn thấy được lại là phần đang mờ — nó đọc ra như một bóng ma
        /// trôi vào, không phải một tấm thẻ.</para>
        /// </summary>
        public float IntroFadeFraction = 1f;
        public float IntroStartScale = 0.96f;
        public float IntroOvershoot = 1.2f;

        // ---------------------------------------------------------------- Camera
        public float FollowSmoothTime = 0.12f;
        public float ScrollToLocalDuration = 0.45f;

        // ---------------------------------------------------------------- Hiệu ứng trên row
        public float PillPopDuration = 0.3f;
        public float PillHoldDuration = 1.1f;
        public float PillRiseDuration = 0.3f;
        public float ShineDuration = 0.55f;
        public float RankRollDuration = 0.12f;
        public float BadgePunchDuration = 0.35f;

        // ---------------------------------------------------------------- Đường cong vẽ tay (0.4.0, mọi thứ opt-in)
        //
        // Lớp UI đổi AnimationCurve trong inspector (LeaderboardMotionConfig) sang KeyframeCurve lúc chụp cấu hình; view-model
        // không có Unity nên không serialize chúng ở đây. null = tắt, giữ easing cũ bit-by-bit.

        /// <summary>Đường cong của ĐỘ LÙI lúc trượt vào (0 → 1). Có thì thay OutCubic / OutBack(<see cref="IntroSlideOvershoot"/>).</summary>
        [NonSerialized] public KeyframeCurve IntroSlideCurve;

        /// <summary>Đường cong cú NHẤC (cỡ 1 → <see cref="LiftScale"/>). Có thì thay OutCubic.</summary>
        [NonSerialized] public KeyframeCurve LiftCurve;

        /// <summary>Đường cong tiến độ cú LEO (ô xuất phát → ô đích). Có thì thay InOut theo <see cref="ClimbEasePower"/>.</summary>
        [NonSerialized] public KeyframeCurve ClimbCurve;

        /// <summary>
        /// Đường cong cú ĐÁP: cỡ = lerp(cỡ lúc đáp, 1, curve(p)). Có thì thay cả OutBack lẫn cú đáp ba đoạn
        /// (<see cref="LandPeakScale"/>). Giá trị ngoài [0, 1] là vọt/hụt — ví dụ đường "đóng dấu" đi xuống −1,46
        /// rồi lên 1,86, tức cỡ 1,05 → 1,12 → 0,957 → 1.
        /// </summary>
        [NonSerialized] public KeyframeCurve LandCurve;

        /// <summary>
        /// Cộng thêm vào độ trễ intro của MỌI row trong list (giây). 0 (mặc định) = như cũ. Ví dụ host để hai nhịp đầu
        /// cho bục (#2, #1, #3 lệch 0,03 s) thì thanh đầu list bắt đầu ở 2 × 0,03 s.
        /// </summary>
        public float IntroRowDelayOffset;

        /// <summary>
        /// Đợt trượt vào gồm những row mà một list ẢO HOÁ đang GIỮ view, thay vì những row vừa khung nhìn. false (mặc định) =
        /// như cũ (<c>VirtualListLayout.VisibleRowCapacity</c> + 1 ô tính từ ô trên cùng đang thấy).
        ///
        /// <para>Bật thì cửa sổ là từ ô CHỨA điểm <see cref="IntroBufferAbove"/> phía trên mép trên khung nhìn tới ô cuối có mép
        /// trên cách mép dưới khung nhìn không quá đệm dưới — <see cref="IntroBufferBelow"/> khi list mở ở đỉnh (cuộn 0),
        /// <see cref="IntroBufferBelowRecentred"/> khi list mở ở một chỗ cuộn khác 0 (đã canh giữa lại, nên còn giữ các view tạo
        /// ở chỗ cũ tới khoảng cách thu hồi). Row thứ k của cửa sổ (tính từ đầu cửa sổ, kể cả row nằm ngoài khung nhìn) trễ
        /// <see cref="IntroRowDelayOffset"/> + k × <see cref="IntroStagger"/>, và mốc "đợt trượt xong"
        /// (<c>BoardModel.IntroSettleSeconds</c>) là lúc row CUỐI cửa sổ đậu. Ô host trình bày (bục) không nằm trong cửa sổ.</para>
        ///
        /// <para>Vì sao: một game tham chiếu trượt vào đúng các view list của nó đang giữ (tạo trong 200, thu hồi ngoài 300 đơn vị
        /// quanh khung nhìn) và chờ view cuối đậu rồi mới đi nhịp kế — nên ba hàng dưới bục khi mở ở đỉnh, năm hàng khi mở ở
        /// hạng 4, tám hàng khi mở giữa list: nhịp kế đến ở 0,62 / 0,68 / 0,77 s thay vì một mốc chung.</para>
        /// </summary>
        public bool IntroUsesListBuffer;

        /// <summary>Đệm phía TRÊN khung nhìn của cửa sổ trượt vào (<see cref="IntroUsesListBuffer"/>), đơn vị canvas.</summary>
        public float IntroBufferAbove = 200f;

        /// <summary>Đệm phía DƯỚI khung nhìn khi list mở ở đỉnh (<see cref="IntroUsesListBuffer"/>), đơn vị canvas.</summary>
        public float IntroBufferBelow = 200f;

        /// <summary>Đệm phía DƯỚI khung nhìn khi list mở ở một chỗ cuộn khác 0 (<see cref="IntroUsesListBuffer"/>).</summary>
        public float IntroBufferBelowRecentred = 300f;

        /// <summary>
        /// Người bị vượt ĐỨNG YÊN (giữ cả số hạng cũ) suốt cú leo trong list; tới lúc đáp, họ cùng dời xuống một ô trong
        /// <see cref="LandPassSlideDuration"/> giây theo đường thẳng, và chỉ đổi số hạng khi màn diễn chốt. false (mặc định) =
        /// như cũ: ai bị vượt thì nhường chỗ ngay lúc đó (<see cref="PassSlideDuration"/>).
        ///
        /// <para>Ẩn dụ: row mình là một tấm thẻ nổi đứng giữa màn, danh sách chạy bên dưới; cả bảng không
        /// "xếp lại" từng người mà chỉ khép chỗ đúng một lần khi thẻ hạ xuống.</para>
        /// </summary>
        public bool DeferPassSlidesToLand;

        /// <summary>Thời gian người bị vượt dời xuống một ô lúc đáp (khi <see cref="DeferPassSlidesToLand"/> bật). Tuyến tính.</summary>
        public float LandPassSlideDuration = 0.12f;

        /// <summary>
        /// Nhịp <c>Pass</c> phát theo ĐỒNG HỒ trong lúc leo (mỗi <c>ClimbTickInterval</c> giây, bắt đầu ngay đầu cú leo, dừng
        /// trước khi còn một nhịp) thay vì mỗi lần vượt một người. 0 (mặc định) = như cũ.
        ///
        /// <para>Ví dụ: tick ngắn + rung mỗi 0,18 s trong max(0,18, thời lượng − 0,18) giây — nhịp đều như tiếng xe chạy,
        /// không phụ thuộc số người bị vượt (cuộn 43 hàng không thành một tràng 43 tiếng).</para>
        /// </summary>
        public float ClimbTickInterval;

        /// <summary>
        /// Glow của row mình chạy theo ĐỒNG HỒ RIÊNG thay vì theo pha: hiện dần trong <c>GlowFadeInDuration</c> giây từ lúc
        /// nhấc (smoothstep), giữ sáng qua cú leo, rồi từ lúc đáp chờ <see cref="GlowFadeOutDelay"/> giây và tắt dần trong
        /// <see cref="GlowFadeOutDuration"/> giây — kể cả sau khi màn diễn đã kết thúc. 0 (mặc định) = như cũ (glow đi theo
        /// pha Lift / Land).
        /// </summary>
        public float GlowFadeInDuration;

        /// <summary>Tính từ lúc đáp, glow còn giữ sáng bao lâu trước khi tắt (chỉ dùng khi <see cref="GlowFadeInDuration"/> &gt; 0).</summary>
        public float GlowFadeOutDelay;

        /// <summary>Glow tắt dần trong bao lâu (chỉ dùng khi <see cref="GlowFadeInDuration"/> &gt; 0).</summary>
        public float GlowFadeOutDuration = 0.5f;

        /// <summary>
        /// Row mình không đổi hạng (Unchanged / RankDown): thay cú nhún sin (<see cref="BobDuration"/>) bằng đúng cú nhấc +
        /// cú đáp của một lần lên hạng (<see cref="LiftCurve"/> trong <see cref="LiftDuration"/>, rồi
        /// <see cref="LandCurve"/> trong <see cref="LandDuration"/>), và KHÔNG sáng glow. false (mặc định) = như cũ.
        /// </summary>
        public bool QuietPulseInsteadOfBob;

        // ---------------------------------------------------------------- Cú lên bục theo một game tham chiếu (0.6.0, opt-in)
        //
        // Ba cờ cho màn lên hạng ĐÁP vào phần host trình bày (HostPresentedTopRanks). Tắt cả ba (mặc định) thì quỹ đạo, nhịp và
        // hình trùng khít 0.5.0 — chốt bằng các dấu vân tay cờ-tắt sẵn có (không ghi lại).

        /// <summary>
        /// Những người bị vượt ở đoạn leo TRONG LIST của một màn lên bục ĐỨNG YÊN (giữ cả số hạng cũ) cho tới lúc host thả cổng
        /// bục; đúng tick được thả họ cùng xuống một ô với số hạng thật — theo <see cref="PodiumPassSlideDuration"/> (0 = đặt
        /// thẳng) và không nhịp <c>Pass</c> nào. false (mặc định) = như 0.5.0: ai bị vượt thì nhường chỗ ngay lúc đó.
        ///
        /// <para>Ẩn dụ: row mình là tấm thẻ NỔI trôi tới ranh giới bục, danh sách bên dưới không xếp lại; host đặt người chơi
        /// lên bục rồi cả bảng về trạng thái cuối trong MỘT frame (cùng frame host dựng lại bục). Chỉ áp cho màn lên bục —
        /// màn leo trong list có cờ riêng (<see cref="DeferPassSlidesToLand"/>).</para>
        /// </summary>
        public bool DeferPodiumApproachPasses;

        /// <summary>
        /// Tốc độ (đơn vị canvas / giây) của cú TIẾP CẬN ranh giới bục kiểu cuộn. 0 (mặc định) = tắt, như 0.5.0.
        ///
        /// <para>Bật (&gt; 0) thì màn lên hạng đáp vào bục từ một ô ≥ <c>BoardModel.HiddenLeadingSlots</c> (bắt đầu TRONG list):</para>
        /// <list type="bullet">
        /// <item>camera mở màn canh giữa ô xuất phát — kể cả khi đó chính là ô ranh giới;</item>
        /// <item>đoạn leo trong list LUÔN có (xuất phát ở ô ranh giới thì là một cú leo không vượt ai), dài đúng quãng cuộn
        /// từ chỗ canh giữa đó về đỉnh list chia cho tốc độ này — không kẹp theo <see cref="ClimbDurationMinimum"/> /
        /// <see cref="ClimbDurationMaximum"/>;</item>
        /// <item>camera và Slot của row mình đi theo CÙNG một tiến độ (<see cref="ClimbCurve"/>) từ cùng một tick
        /// (<c>BoardModel.PodiumApproachProgress</c>), nên trên màn hình row đi đúng một đường thẳng từ chỗ canh giữa tới ô
        /// ranh giới;</item>
        /// <item>nhịp <c>Pass</c> theo đồng hồ (<see cref="ClimbTickInterval"/>) tính trên thời lượng đó — thời lượng 0 vẫn có
        /// đúng một nhịp.</item>
        /// </list>
        /// <para>Quãng cuộn do list báo (<c>BoardModel.SetPodiumApproachStartScroll</c>, <c>LeaderboardScrollView</c> tự ghi lúc
        /// dựng). Model không có list nào báo (tự lái bằng tay) thì thời lượng rơi về công thức leo thường.</para>
        /// </summary>
        public float PodiumApproachScrollSpeed;

        /// <summary>
        /// Chỗ DỪNG của row mình ở cuối cú tiếp cận (<see cref="PodiumApproachScrollSpeed"/> &gt; 0), tính bằng PHẦN của một bước
        /// hàng so với ô ranh giới: âm = dừng CAO hơn ô ranh giới. 0 (mặc định) = dừng đúng ô ranh giới, như 0.5.0.
        ///
        /// <para>Vì sao cần: một game tham chiếu tính đích của cú tiếp cận bằng một chiều cao vùng bục NGẮN hơn ảnh thật của nó,
        /// nên thẻ nổi dừng cao hơn ô hạng đầu của list một khoảng lẻ (ví dụ −8 / 224 bước). Host muốn vừa giữ ảnh thật của list
        /// vừa dừng đúng chỗ đó thì đặt số này. Chỉ đổi Slot của row mình trong cú tiếp cận và lúc đứng chờ ở cổng; camera, thời
        /// lượng, luật vượt và mọi row khác không đổi, và pha sau cổng đi tiếp từ đúng chỗ đang đứng.</para>
        /// </summary>
        public float PodiumApproachStopOffsetRows;

        /// <summary>
        /// Quãng HỤT của cú tiếp cận kiểu cuộn (<see cref="PodiumApproachScrollSpeed"/> &gt; 0), tính bằng PHẦN của một bước hàng —
        /// chỉ áp khi lúc bắt đầu, vùng host trình bày ĐÃ trôi hẳn khỏi khung nhìn (chỗ cuộn ≥ mép trên ô ranh giới). 0 (mặc định)
        /// = tắt: camera luôn về đỉnh list, như 0.6.0.
        ///
        /// <para>Vì sao cần: một game tham chiếu đo quãng cuộn còn lại bằng chiều cao MODEL của các ô đứng trước ô đang thấy đầu
        /// tiên, và ô vùng bục trong model ngắn hơn ảnh thật của nó. Khi ô đó đã khuất lúc bắt đầu, quãng đo được hụt một khoảng
        /// lẻ (ví dụ 11 / 224 bước): list dừng CÁCH đỉnh đúng khoảng đó và cú tiếp cận ngắn đi tương ứng. Bật số này thì: camera
        /// dừng ở (số này × bước hàng) thay vì 0 và đứng yên ở đó, thời lượng = (quãng cuộn − khoảng đó) / tốc độ, còn chỗ dừng của
        /// row mình dời theo đúng khoảng đó — trên màn hình row vẫn dừng đúng chỗ cũ, chỉ vùng host (và mọi thứ trong list) cao
        /// hơn khoảng đó. Lúc bắt đầu vùng host còn thấy được thì không áp; không có cú tiếp cận thì không áp.</para>
        /// </summary>
        public float PodiumApproachShortfallRows;

        /// <summary>
        /// Row mình do host trình bày (<c>BoardModel.ListPresence</c> = 0 — đang đứng trên bục) thì pha Bob của lượt KHÔNG đổi
        /// chỗ (ScoreImproved, Unchanged, RankDown) dài 0 giây — cả cú nhún sin lẫn nhịp nhẹ <see cref="QuietPulseInsteadOfBob"/>.
        /// false (mặc định) = như 0.5.0.
        ///
        /// <para>Vì sao: row đó nằm SAU bục, không ai thấy cú nhún, mà màn diễn vẫn chờ nó (nhịp nhẹ mặc định 0,2 + 0,22 s) trước
        /// khi báo xong. Host có hiệu ứng của riêng nó trên bục thì chính hiệu ứng đó phải quyết định lúc kết thúc.</para>
        /// </summary>
        public bool HostPresentedRowSkipsQuietPulse;

        /// <summary>
        /// Các pha của màn diễn đi theo NHỊP KHUNG của coroutine thay vì đồng hồ liên tục. false (mặc định) = như 0.5.0.
        ///
        /// <para>Bật thì:</para>
        /// <list type="bullet">
        /// <item>pha nào bắt đầu vì host thả cổng (<see cref="RevealTimeline.ReleaseHostHold"/> /
        /// <see cref="RevealTimeline.ReleasePodiumHold"/>) hoặc vì pha trước dài 0 giây thì được tính luôn độ dài của CHÍNH khung
        /// đó — khung đầu vẽ ở giây dt của pha, không phải giây 0;</item>
        /// <item>pha có độ dài vẽ mẫu cuối (tiến độ 1) ở khung nó hết giờ, và pha kế bắt đầu ở khung SAU (nhịp của pha kế phát
        /// ở khung đó), với giây dt của khung ấy;</item>
        /// <item>nhịp tick theo đồng hồ của cú leo (<see cref="ClimbTickInterval"/>) phát tick đầu ở khung đầu của pha, rồi mỗi
        /// tick sau ở khung đầu tiên cách khung của tick trước ít nhất một interval (đồng hồ hẹn lại từ khung nó nổ) — ở 60 Hz và
        /// 0,18 s là đúng mỗi 11 khung.</item>
        /// </list>
        /// <para>Vì sao: một game tham chiếu diễn mỗi pha bằng một coroutine <c>elapsed += dt; vẽ; yield</c> gọi thẳng từ callback
        /// của pha trước. Đồng hồ liên tục cho cùng các mốc nhịp nhưng vẽ mọi pha chậm một khung — ở cú tiếp cận 243 đơn vị trong
        /// 6 khung là lệch tới ~55 đơn vị giữa chừng. Số khung của các pha, số nhịp và trạng thái cuối không đổi.</para>
        /// </summary>
        public bool CoroutineFrameTiming;

        /// <summary>
        /// Khi &gt; 0 (và <see cref="CoroutineFrameTiming"/> bật): tick theo đồng hồ của cú leo nằm trên LƯỚI KHUNG của tốc độ này thay vì
        /// hẹn lại từ khung nổ. Khoảng <see cref="ClimbTickInterval"/> được làm tròn LÊN số khung của lưới (0,18 s ở 60 = 11 khung =
        /// 0,1833 s), và tick thứ k nổ ở khung GẦN mốc k × khoảng đó nhất (sai nửa khung của lưới) — tính dồn từ khung đầu của pha.
        /// 0 (mặc định) = như cũ.
        ///
        /// <para>Vì sao: đồng hồ thật của Editor/thiết bị không đều 1/60. Hẹn lại từ khung nổ thì mỗi khung ngắn hơn 1/60 một chút đẩy
        /// tick sang khung thứ 12, và độ trễ CỘNG DỒN qua cả cú leo (đo được +100 ms sau 14 tick). Lưới dồn giữ mọi tick trong một khung
        /// của mốc, không trôi.</para>
        /// </summary>
        public float ClimbTickFrameRate;

        public float PillTotalDuration => PillPopDuration + PillHoldDuration + PillRiseDuration;

        public MotionSettings Clone()
        {
            return (MotionSettings)MemberwiseClone();
        }
    }
}
