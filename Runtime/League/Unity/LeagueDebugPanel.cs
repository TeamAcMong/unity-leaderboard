using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace DreamTech.Leaderboard.League.Unity
{
    /// <summary>
    /// Bảng thử League vẽ bằng IMGUI: bày mọi thao tác của <see cref="LeagueSystem"/> ra nút và hiển thị bảng nhóm, kết quả mùa,
    /// quà. Dùng khi chưa có UI theo design, và để thử luật ngay trong game thật (bật bằng cheat).
    ///
    /// <para>Không phải UI sản phẩm: không theo design, không tối ưu. Nút nào cần dịch vụ mô phỏng hoặc đồng hồ tua được thì chỉ
    /// hiện khi <see cref="Bind"/> có truyền chúng vào.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LeagueDebugPanel : MonoBehaviour
    {
        private const float ReferenceScreenHeight = 1920f;
        private const int MaximumLogLines = 6;
        private const int SmallTimeStepHours = 1;
        private const int LargeTimeStepHours = 6;

        /// <summary>"→ hết mùa" tua quá mốc kết thúc chừng này để chắc chắn đã sang mùa sau.</summary>
        private static readonly TimeSpan SeasonEndOvershoot = TimeSpan.FromMinutes(1);

        private readonly List<string> _log = new List<string>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private LeagueSystem _league;
        private SimulatedLeagueGroupService _simulation;
        private OffsetLeagueClock _clock;
        private MonotonicLeagueClock _monotonicClock;
        private ManualLeagueFeatureGate _featureGate;

        private LeaguePageData _page;
        private SeasonResult _pendingResult;
        private Vector2 _standingsScroll;
        private bool _isBusy;
        private bool _isReloadRequested;

        /// <summary>Dòng trạng thái riêng của game (vd ví tiền sau khi nhận quà). Để null thì không hiện.</summary>
        public Func<string> ExtraStatusLine { get; set; }

        public bool IsVisible { get; set; } = true;
        public LeagueSystem League => _league;
        public SimulatedLeagueGroupService Simulation => _simulation;
        public LeaguePageData Page => _page;
        public SeasonResult PendingResult => _pendingResult;

        /// <summary>Đang có lệnh gọi dịch vụ chạy dở.</summary>
        public bool IsBusy => _isBusy;

        public event Action<string> Logged;

        /// <summary>
        /// Huỷ mọi lệnh gọi đang chạy dở khi panel bị huỷ (đổi scene, tắt cheat). Không huỷ thì continuation vẫn chạy sau khi
        /// panel chết và có thể ghi đè dữ liệu cũ lên nơi lưu — lỗi này bắt được nhờ test PlayMode chạy liên tiếp nhiều scene.
        /// </summary>
        private void OnDestroy()
        {
            if (_league != null) _league.TrophyGrantRejected -= OnTrophyGrantRejected;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        /// <summary>Tạo panel trên một GameObject mới và gắn vào hệ thống cho sẵn.</summary>
        public static LeagueDebugPanel Create(LeagueSystem league, SimulatedLeagueGroupService simulation = null,
                                              OffsetLeagueClock clock = null, ManualLeagueFeatureGate featureGate = null,
                                              bool keepAcrossScenes = true)
        {
            var host = new GameObject("LeagueDebugPanel");
            if (keepAcrossScenes) DontDestroyOnLoad(host);
            var panel = host.AddComponent<LeagueDebugPanel>();
            panel.Bind(league, simulation, clock, featureGate);
            return panel;
        }

        /// <summary>
        /// Như bản trên, kèm đồng hồ không-lùi để nút "Xoá dữ liệu" xoá luôn mốc giờ cao nhất. Không truyền thì panel tự nhận ra khi
        /// <see cref="LeagueSystem.Clock"/> chính là một <see cref="MonotonicLeagueClock"/>.
        /// </summary>
        public static LeagueDebugPanel Create(LeagueSystem league, SimulatedLeagueGroupService simulation, OffsetLeagueClock clock,
                                              MonotonicLeagueClock monotonicClock, ManualLeagueFeatureGate featureGate,
                                              bool keepAcrossScenes = true)
        {
            var host = new GameObject("LeagueDebugPanel");
            if (keepAcrossScenes) DontDestroyOnLoad(host);
            var panel = host.AddComponent<LeagueDebugPanel>();
            panel.Bind(league, simulation, clock, monotonicClock, featureGate);
            return panel;
        }

        /// <param name="simulation">Có thì hiện thêm nút của dữ liệu giả lập (leo hạng, lỗi mạng, xoá dữ liệu).</param>
        /// <param name="clock">Có thì hiện thêm nút tua giờ.</param>
        /// <param name="featureGate">Có thì hiện thêm nút khoá / mở tính năng.</param>
        public void Bind(LeagueSystem league, SimulatedLeagueGroupService simulation = null, OffsetLeagueClock clock = null,
                         ManualLeagueFeatureGate featureGate = null)
        {
            Bind(league, simulation, clock, null, featureGate);
        }

        /// <param name="monotonicClock">
        /// Đồng hồ không-lùi của League. "Xoá dữ liệu" gọi <see cref="MonotonicLeagueClock.ResetHighWater"/> — không xoá mốc thì
        /// League kẹt ở giờ đã tua tới dù offset đã về 0. Null thì dùng <see cref="LeagueSystem.Clock"/> nếu nó là đồng hồ không-lùi.
        /// </param>
        public void Bind(LeagueSystem league, SimulatedLeagueGroupService simulation, OffsetLeagueClock clock,
                         MonotonicLeagueClock monotonicClock, ManualLeagueFeatureGate featureGate)
        {
            if (league == null) throw new ArgumentNullException(nameof(league));
            if (_league != null) _league.TrophyGrantRejected -= OnTrophyGrantRejected;
            _league = league;
            _league.TrophyGrantRejected += OnTrophyGrantRejected;
            _simulation = simulation;
            _clock = clock;
            _monotonicClock = monotonicClock ?? league.Clock as MonotonicLeagueClock;
            _featureGate = featureGate;
            Reload();
        }

        // ---------------------------------------------------------------- Thao tác (nút + test dùng chung)

        public void WinLevel(int difficultyIndex)
        {
            if (_league == null) return;
            LevelWinOutcome outcome = _league.RecordLevelWin(new LevelWinContext(0, difficultyIndex));
            Log(outcome.Awarded
                ? "Thắng level: +" + outcome.Trophies + " cúp, streak " + outcome.StreakBefore.Level + " → " + outcome.StreakAfter.Level
                : "Thắng level nhưng League đang khoá: không cộng gì");
            Reload();
        }

        public void RaiseStreakEvent(WinStreakEvent streakEvent)
        {
            if (_league == null) return;
            bool wouldWarn = _league.WouldLoseStreak(streakEvent);
            WinStreakChange change = _league.RecordStreakEvent(streakEvent);
            Log(streakEvent + ": streak " + change.Before.Level + " → " + change.After.Level +
                (wouldWarn ? " (popup thật sẽ cảnh báo trước)" : string.Empty));
            Reload();
        }

        /// <summary>Cộng thẳng vào độ lệch của đồng hồ tua được (giờ máy + offset), không bù phần đang chậm hơn mốc không-lùi.</summary>
        public void AdvanceTime(TimeSpan duration)
        {
            if (_clock == null) return;
            _clock.Advance(duration);
            Log("Tua giờ +" + FormatDuration(duration));
            Reload();
        }

        /// <summary>
        /// Cho giờ League tiến đúng <paramref name="duration"/>. Giờ máy + offset đang chậm hơn mốc của đồng hồ không-lùi (đã tua
        /// lùi, chỉnh giờ máy) thì League đứng yên ở mốc — cộng thêm phần chậm đó, không thì bấm "+1h" League không nhúc nhích.
        /// </summary>
        public void AdvanceLeagueTime(TimeSpan duration)
        {
            if (_clock == null) return;
            TimeSpan behindHighWater = TimeBehindHighWater();
            _clock.Advance(behindHighWater + duration);
            Log("Tua giờ League +" + FormatDuration(duration) +
                (behindHighWater > TimeSpan.Zero ? " (bù " + FormatDuration(behindHighWater) + " giờ máy chậm hơn mốc)" : string.Empty));
            Reload();
        }

        /// <summary>
        /// Tua tới ngay sau lúc mùa hiện tại (theo giờ League) kết thúc. Khoảng tua tính từ giờ của đồng hồ tua được, không từ
        /// <see cref="LeagueSystem.TimeLeftInCurrentSeason"/>: giờ máy đang chậm hơn mốc không-lùi thì "thời gian còn lại" đếm từ mốc,
        /// cộng đúng chừng đó vẫn chưa tới cuối mùa.
        /// </summary>
        public void AdvanceToSeasonEnd()
        {
            if (_league == null || _clock == null) return;
            DateTime target = _league.CurrentSeason.EndUtc + SeasonEndOvershoot;
            TimeSpan distance = target - _clock.UtcNow;
            if (distance > TimeSpan.Zero) AdvanceTime(distance);
            else Reload();
        }

        /// <summary>Phần giờ máy + offset đang chậm hơn mốc của đồng hồ không-lùi; 0 nếu không có đồng hồ đó hoặc không chậm.</summary>
        private TimeSpan TimeBehindHighWater()
        {
            if (_monotonicClock == null) return TimeSpan.Zero;
            DateTime highWaterUtc = _monotonicClock.HighWaterUtc;
            if (highWaterUtc == DateTime.MinValue) return TimeSpan.Zero;
            TimeSpan behind = highWaterUtc - _clock.UtcNow;
            return behind > TimeSpan.Zero ? behind : TimeSpan.Zero;
        }

        /// <param name="holdUntilSeasonEnd">
        /// false = đủ để đứng hạng đó NGAY BÂY GIỜ (bot còn kiếm tiếp nên hạng sẽ trôi);
        /// true = đủ để GIỮ hạng đó tới lúc mùa kết thúc.
        /// </param>
        public void ClimbToRank(int oneBasedRank, bool holdUntilSeasonEnd = false)
        {
            if (_league == null || _simulation == null) return;
            long trophies = _simulation.DebugTrophiesToReachRank(_league.CurrentSeason, Mathf.Max(0, oneBasedRank - 1), holdUntilSeasonEnd);
            _simulation.DebugSetLocalTrophies(_league.CurrentSeason, trophies);
            Log("Đặt cúp = " + trophies + " để đứng hạng #" + oneBasedRank +
                (holdUntilSeasonEnd ? " tới hết mùa" : " lúc này (bot còn kiếm tiếp, hạng sẽ trôi)"));
            Reload();
        }

        /// <summary>
        /// Tải lại trang + kết quả mùa. Gọi lúc đang gọi dở thì KHÔNG bỏ qua mà chạy thêm một vòng sau đó — nếu bỏ qua, thao tác
        /// vừa bấm sẽ không bao giờ lên bảng.
        /// </summary>
        public async void Reload()
        {
            if (_league == null) return;
            _isReloadRequested = true;
            if (_isBusy) return;

            _isBusy = true;
            try
            {
                while (_isReloadRequested)
                {
                    _isReloadRequested = false;
                    // Bắt lỗi TRONG vòng lặp: một lần gọi hỏng không được nuốt luôn yêu cầu tải lại đang xếp hàng.
                    try
                    {
                        _page = await _league.LoadPageAsync(_lifetime.Token);
                        _pendingResult = await _league.GetPendingSeasonResultAsync(_lifetime.Token);
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        // Gồm cả lượt gọi cũ bị dịch vụ mô phỏng bỏ vì dữ liệu vừa bị xoá (SimulatedLeagueException, lỗi tạm): ghi một
                        // dòng rồi đi tiếp — vòng sau (do nút xoá yêu cầu) tải lại dữ liệu mới.
                        Log("Lỗi tải: " + exception.GetBaseException().Message);
                    }
                }
            }
            finally
            {
                _isBusy = false;
            }
        }

        public async void AcknowledgeResult()
        {
            if (_league == null || _pendingResult == null) return;
            string seasonId = _pendingResult.SeasonId;
            try
            {
                await _league.AcknowledgeSeasonResultAsync(seasonId, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Lỗi mạng giả lập, hoặc lượt gọi cũ bị bỏ sau "Xoá dữ liệu": async void không được để lọt exception.
                Log("Lỗi xem kết quả: " + exception.GetBaseException().Message);
                return;
            }
            Log("Đã xem kết quả mùa " + seasonId);
            Reload();
        }

        public async void ClaimReward()
        {
            if (_league == null || _pendingResult == null) return;
            LeagueClaimOutcome outcome;
            try
            {
                outcome = await _league.ClaimSeasonRewardAsync(_pendingResult.SeasonId, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Log("Lỗi nhận rương: " + exception.GetBaseException().Message);
                return;
            }
            Log("Nhận rương: " + outcome.Status + " " + outcome.Package);
            Reload();
        }

        public void FailNextCall()
        {
            if (_simulation == null) return;
            _simulation.FailNextCall();
            Log("Lần gọi kế tiếp sẽ lỗi mạng");
        }

        public void ToggleFeatureGate()
        {
            if (_featureGate == null) return;
            _featureGate.IsUnlocked = !_featureGate.IsUnlocked;
            Log("League " + (_featureGate.IsUnlocked ? "đã mở" : "đang khoá"));
        }

        /// <summary>Xoá dữ liệu mô phỏng + trạng thái trên máy, đưa đồng hồ về giờ thật (offset về 0, xoá mốc giờ cao nhất).</summary>
        public void ResetEverything()
        {
            if (_simulation != null) _simulation.DebugResetSimulation();
            if (_league != null) _league.DebugClearLocalState();
            if (_clock != null) _clock.Offset = TimeSpan.Zero;
            if (_monotonicClock != null) _monotonicClock.ResetHighWater();
            _pendingResult = null;
            Log("Đã xoá sạch dữ liệu League");
            Reload();
        }

        private void OnTrophyGrantRejected(LeagueTrophyGrant grant, LeagueTrophyGrantRejection reason)
        {
            Log("Dịch vụ từ chối " + grant.Trophies + " cúp của mùa " + grant.SeasonId + ": " + reason);
        }

        public void Log(string message)
        {
            _log.Add(message);
            if (_log.Count > MaximumLogLines) _log.RemoveAt(0);
            Debug.Log("[League] " + message);
            Logged?.Invoke(message);
        }

        // ---------------------------------------------------------------- Vẽ

        private void OnGUI()
        {
            if (!IsVisible || _league == null) return;

            float scale = Mathf.Max(0.5f, Screen.height / ReferenceScreenHeight);
            GUI.skin.button.fontSize = Mathf.RoundToInt(26 * scale);
            GUI.skin.label.fontSize = Mathf.RoundToInt(24 * scale);
            GUI.skin.box.fontSize = Mathf.RoundToInt(24 * scale);
            float buttonHeight = 60f * scale;
            float columnWidth = Screen.width * 0.46f;

            GUILayout.BeginArea(new Rect(10f, 10f, columnWidth, Screen.height - 20f), GUI.skin.box);
            DrawStandings();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(columnWidth + 20f, 10f, Screen.width - columnWidth - 30f, Screen.height - 20f), GUI.skin.box);
            DrawStatus();
            DrawControls(buttonHeight);
            DrawResult(buttonHeight);
            DrawLog();
            GUILayout.EndArea();
        }

        private void DrawStandings()
        {
            if (_page == null)
            {
                GUILayout.Label("Đang tải bảng...");
                return;
            }

            GUILayout.Label("NHÓM — " + _page.Tier.TierId + " · " + _page.Group.GroupSize + " người");
            _standingsScroll = GUILayout.BeginScrollView(_standingsScroll);
            for (int rank = 0; rank < _page.Rows.Count; rank++)
            {
                LeagueStandingRow row = _page.Rows[rank];

                // Dải zone chèn đúng chỗ design vẽ: ngay sau nhóm lên hạng, ngay trước nhóm xuống hạng.
                if (rank == _page.Bands.PromotionEndRank && _page.Bands.HasPromotion) GUILayout.Label("▲ ──── Promotion Zone ────");
                if (rank == _page.Bands.DemotionStartRank && _page.Bands.HasDemotion) GUILayout.Label("▼ ──── Demotion Zone ────");

                string chest = row.Reward.IsEmpty ? string.Empty : "  [" + row.Reward.ChestId + "]";
                string line = "#" + (rank + 1).ToString(CultureInfo.InvariantCulture).PadLeft(2) + "  " +
                              row.Entry.DisplayName.PadRight(10) + " " +
                              row.Entry.Score.ToString(CultureInfo.InvariantCulture).PadLeft(5) + chest;
                if (row.IsLocal) line = "➤ " + line;
                GUILayout.Label(line);
            }
            GUILayout.EndScrollView();
        }

        private void DrawStatus()
        {
            GUILayout.Label("Mùa: " + (_page != null ? _page.Season.SeasonId : "?") + "   còn " + FormatDuration(_league.TimeLeftInCurrentSeason));
            GUILayout.Label("Streak: bậc " + _league.Streak.Level + " (×" + _league.StreakMultiplier + ")" +
                            "   Cúp chưa gửi: " + _league.UnsentTrophies);
            GUILayout.Label("Thoát bây giờ có mất streak: " + (_league.WouldLoseStreak(WinStreakEvent.LevelQuit) ? "CÓ" : "không") +
                            "   League: " + (_league.IsUnlocked ? "mở" : "KHOÁ"));
            if (_monotonicClock != null && _monotonicClock.IsInnerBehind) GUILayout.Label("Giờ máy đang chậm hơn mốc League — League giữ mốc, không lùi");
            if (_league.RejectedTrophyGrantCount > 0) GUILayout.Label("Grant bị từ chối trong phiên: " + _league.RejectedTrophyGrantCount);
            if (ExtraStatusLine != null) GUILayout.Label(ExtraStatusLine() + (_isBusy ? "   (đang gọi...)" : string.Empty));
            else if (_isBusy) GUILayout.Label("(đang gọi...)");
        }

        private void DrawControls(float buttonHeight)
        {
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Thắng thường", GUILayout.Height(buttonHeight))) WinLevel(0);
            if (GUILayout.Button("Thắng khó", GUILayout.Height(buttonHeight))) WinLevel(1);
            if (GUILayout.Button("Thắng siêu khó", GUILayout.Height(buttonHeight))) WinLevel(2);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Thoát level", GUILayout.Height(buttonHeight))) RaiseStreakEvent(WinStreakEvent.LevelQuit);
            if (GUILayout.Button("Thua hẳn", GUILayout.Height(buttonHeight))) RaiseStreakEvent(WinStreakEvent.LevelLost);
            if (GUILayout.Button("Hồi sinh", GUILayout.Height(buttonHeight))) RaiseStreakEvent(WinStreakEvent.LevelRevived);
            GUILayout.EndHorizontal();

            if (_clock != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Tua +1h", GUILayout.Height(buttonHeight))) AdvanceLeagueTime(TimeSpan.FromHours(SmallTimeStepHours));
                if (GUILayout.Button("Tua +6h", GUILayout.Height(buttonHeight))) AdvanceLeagueTime(TimeSpan.FromHours(LargeTimeStepHours));
                if (GUILayout.Button("→ hết mùa", GUILayout.Height(buttonHeight))) AdvanceToSeasonEnd();
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (_simulation != null)
            {
                if (GUILayout.Button("Lên hạng #1", GUILayout.Height(buttonHeight))) ClimbToRank(1);
                if (GUILayout.Button("Giữ #1 hết mùa", GUILayout.Height(buttonHeight))) ClimbToRank(1, holdUntilSeasonEnd: true);
            }
            if (GUILayout.Button("Tải lại", GUILayout.Height(buttonHeight))) Reload();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (_simulation != null && GUILayout.Button("Lỗi mạng lần sau", GUILayout.Height(buttonHeight))) FailNextCall();
            if (_featureGate != null && GUILayout.Button(_league.IsUnlocked ? "Khoá League" : "Mở League", GUILayout.Height(buttonHeight))) ToggleFeatureGate();
            if (GUILayout.Button("Xoá dữ liệu", GUILayout.Height(buttonHeight))) ResetEverything();
            GUILayout.EndHorizontal();
        }

        private void DrawResult(float buttonHeight)
        {
            if (_pendingResult == null) return;
            GUILayout.Space(8f);
            GUILayout.Box("KẾT QUẢ MÙA " + _pendingResult.SeasonId + "\n" +
                          _pendingResult.Outcome + ": bậc " + _pendingResult.TierIndexBefore + " → " + _pendingResult.TierIndexAfter + "\n" +
                          "hạng #" + (_pendingResult.FinalRank + 1) + "/" + _pendingResult.GroupSize + " · " + _pendingResult.FinalTrophies + " cúp\n" +
                          "rương: " + (_pendingResult.Reward.IsEmpty ? "không" : _pendingResult.Reward.ToString()) +
                          (_pendingResult.Acknowledged ? " · đã xem" : string.Empty) +
                          (_pendingResult.RewardClaimed ? " · đã nhận" : string.Empty));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Xem xong", GUILayout.Height(buttonHeight))) AcknowledgeResult();
            if (GUILayout.Button("Nhận rương", GUILayout.Height(buttonHeight))) ClaimReward();
            GUILayout.EndHorizontal();
        }

        private void DrawLog()
        {
            GUILayout.Space(8f);
            for (int index = _log.Count - 1; index >= 0; index--) GUILayout.Label(_log[index]);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            if (duration.TotalHours >= 1) return (int)duration.TotalHours + "h " + duration.Minutes + "m";
            return duration.Minutes + "m " + duration.Seconds + "s";
        }
    }
}
