using System;

namespace DreamTech.Leaderboard.League
{
    /// <summary>
    /// Nơi lắp các khối Lego của League. Mỗi <c>With...</c> là một ổ cắm: đổi module = đổi đúng một dòng ở composition root,
    /// không sửa <see cref="LeagueSystem"/>, UI hay luật khác.
    ///
    /// <para>Bắt buộc: dịch vụ nhóm, lịch mùa, luật cúp (không có giá trị mặc định hợp lý cho game). Còn lại có mặc định:
    /// đồng hồ hệ thống, lưu trong RAM, chưa phát quà (quà nằm chờ), luôn mở khoá, luật streak chuẩn.</para>
    /// </summary>
    public sealed class LeagueSystemBuilder
    {
        private readonly string _systemId;
        private readonly LeagueRules _rules;
        private readonly WinStreakLadder _streakLadder;

        private ILeagueGroupService _groupService;
        private ISeasonSchedule _schedule;
        private ITrophyRule _trophyRule;
        private ILeagueClock _clock;
        private ILeagueTextStore _textStore;
        private ILeagueRewardGranter _rewardGranter;
        private ILeagueFeatureGate _featureGate;
        private IWinStreakRule _streakRule;

        public LeagueSystemBuilder(string systemId, LeagueRules rules, WinStreakLadder streakLadder)
        {
            if (string.IsNullOrEmpty(systemId)) throw new ArgumentException("System id không được rỗng.", nameof(systemId));
            _systemId = systemId;
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _streakLadder = streakLadder ?? throw new ArgumentNullException(nameof(streakLadder));
        }

        public LeagueSystemBuilder WithGroupService(ILeagueGroupService groupService)
        {
            _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
            return this;
        }

        public LeagueSystemBuilder WithSchedule(ISeasonSchedule schedule)
        {
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            return this;
        }

        public LeagueSystemBuilder WithTrophyRule(ITrophyRule trophyRule)
        {
            _trophyRule = trophyRule ?? throw new ArgumentNullException(nameof(trophyRule));
            return this;
        }

        public LeagueSystemBuilder WithClock(ILeagueClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            return this;
        }

        public LeagueSystemBuilder WithTextStore(ILeagueTextStore textStore)
        {
            _textStore = textStore ?? throw new ArgumentNullException(nameof(textStore));
            return this;
        }

        public LeagueSystemBuilder WithRewardGranter(ILeagueRewardGranter rewardGranter)
        {
            _rewardGranter = rewardGranter ?? throw new ArgumentNullException(nameof(rewardGranter));
            return this;
        }

        public LeagueSystemBuilder WithFeatureGate(ILeagueFeatureGate featureGate)
        {
            _featureGate = featureGate ?? throw new ArgumentNullException(nameof(featureGate));
            return this;
        }

        public LeagueSystemBuilder WithWinStreakRule(IWinStreakRule streakRule)
        {
            _streakRule = streakRule ?? throw new ArgumentNullException(nameof(streakRule));
            return this;
        }

        public LeagueSystem Build()
        {
            if (_groupService == null) throw new InvalidOperationException("Thiếu dịch vụ nhóm: gọi WithGroupService.");
            if (_schedule == null) throw new InvalidOperationException("Thiếu lịch mùa: gọi WithSchedule.");
            if (_trophyRule == null) throw new InvalidOperationException("Thiếu luật cúp: gọi WithTrophyRule.");

            return new LeagueSystem(_systemId, _rules, _streakLadder, _groupService, _schedule,
                                    _clock ?? new SystemLeagueClock(),
                                    _textStore ?? new InMemoryLeagueTextStore(),
                                    _rewardGranter ?? new DeferredLeagueRewardGranter(),
                                    _featureGate ?? new ManualLeagueFeatureGate(true),
                                    _streakRule ?? new StandardWinStreakRule(),
                                    _trophyRule);
        }
    }
}
