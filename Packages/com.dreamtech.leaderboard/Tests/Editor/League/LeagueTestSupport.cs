using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamTech.Leaderboard.League.Tests
{
    /// <summary>Dựng bộ luật và hệ thống League giống design (5 tier, streak 5 bậc) để các test dùng chung.</summary>
    internal static class LeagueTestFactory
    {
        /// <summary>Thứ Hai 2026-01-05 00:00 UTC.</summary>
        public static readonly DateTime Anchor = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        public static readonly TimeSpan SeasonLength = TimeSpan.FromDays(7);

        public const string RegularChest = "chest.regular";
        public const string GoldChest = "chest.gold";

        public static LeagueLadder CreateLadder()
        {
            return new LeagueLadder(new[]
            {
                new LeagueTierDefinition("bronze", 5, 5),
                new LeagueTierDefinition("silver", 5, 5),
                new LeagueTierDefinition("gold", 5, 5),
                new LeagueTierDefinition("platinum", 5, 5),
                new LeagueTierDefinition("diamond", 5, 5),
            });
        }

        public static WinStreakLadder CreateStreakLadder(LeagueRewardPackage levelTwoReward = null)
        {
            return new WinStreakLadder(new[]
            {
                new WinStreakStep(1),
                new WinStreakStep(2, levelTwoReward),
                new WinStreakStep(3),
                new WinStreakStep(4),
                new WinStreakStep(5),
            });
        }

        public static RankBracketRewardTable CreateRewardTable()
        {
            return new RankBracketRewardTable(new[]
            {
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 0, 0,
                                        new LeagueRewardPackage(GoldChest, new[] { new LeagueRewardItem("coin", 100) })),
                new LeagueRewardBracket(LeagueRewardBracket.AnyTier, 1, LeagueRewardBracket.ToLastRank,
                                        new LeagueRewardPackage(RegularChest, new[] { new LeagueRewardItem("coin", 10) })),
            });
        }

        public static LeagueRules CreateRules(ISeasonOutcomeRule outcomeRule = null)
        {
            return new LeagueRules(CreateLadder(), outcomeRule: outcomeRule, rewardTable: CreateRewardTable());
        }

        public static FixedLengthSeasonSchedule CreateSchedule()
        {
            return new FixedLengthSeasonSchedule(Anchor, SeasonLength);
        }

        public static ManualLeagueClock CreateClockInFirstSeason()
        {
            return new ManualLeagueClock(Anchor + TimeSpan.FromDays(1));
        }

        public static SimulatedLeagueOptions CreateSimulationOptions()
        {
            return new SimulatedLeagueOptions
            {
                GroupSize = 30,
                Seed = 5,
                LatencyMilliseconds = 0,
            };
        }
    }

    /// <summary>Một lần dựng đầy đủ: đồng hồ, lưu trữ, dịch vụ mô phỏng (có đếm lượt gọi), granter ghi lại.</summary>
    internal sealed class LeagueScenario
    {
        public const string SystemId = "test-league";

        public ManualLeagueClock Clock;
        public FixedLengthSeasonSchedule Schedule;
        public InMemoryLeagueTextStore Store;
        public LeagueRules Rules;
        public WinStreakLadder StreakLadder;
        public SimulatedLeagueGroupService Simulation;
        public CountingLeagueGroupService Service;
        public RecordingLeagueRewardGranter Granter;
        public ManualLeagueFeatureGate Gate;
        public LeagueSystem System;

        public static LeagueScenario Create(SimulatedLeagueOptions options = null, LeagueRewardPackage streakLevelTwoReward = null,
                                            InMemoryLeagueTextStore store = null, ManualLeagueClock clock = null)
        {
            var scenario = new LeagueScenario
            {
                Clock = clock ?? LeagueTestFactory.CreateClockInFirstSeason(),
                Schedule = LeagueTestFactory.CreateSchedule(),
                Store = store ?? new InMemoryLeagueTextStore(),
                Rules = LeagueTestFactory.CreateRules(),
                StreakLadder = LeagueTestFactory.CreateStreakLadder(streakLevelTwoReward),
                Granter = new RecordingLeagueRewardGranter(),
                Gate = new ManualLeagueFeatureGate(true),
            };
            scenario.Simulation = new SimulatedLeagueGroupService(options ?? LeagueTestFactory.CreateSimulationOptions(), scenario.Rules,
                                                                  scenario.Clock, scenario.Store);
            scenario.Service = new CountingLeagueGroupService(scenario.Simulation);
            scenario.System = scenario.Rebuild();
            return scenario;
        }

        /// <summary>Dựng lại LeagueSystem trên cùng nơi lưu (giả lập mở lại app).</summary>
        public LeagueSystem Rebuild()
        {
            System = new LeagueSystemBuilder(SystemId, Rules, StreakLadder)
                     .WithGroupService(Service)
                     .WithSchedule(Schedule)
                     .WithClock(Clock)
                     .WithTextStore(Store)
                     .WithRewardGranter(Granter)
                     .WithFeatureGate(Gate)
                     .WithTrophyRule(new MultipliedTrophyRule(new[] { 10, 15, 20 }))
                     .Build();
            return System;
        }

        public void AdvancePastSeasonEnd()
        {
            SeasonWindow season = Schedule.GetSeasonAt(Clock.UtcNow);
            Clock.Set(season.EndUtc + TimeSpan.FromHours(1));
        }
    }

    internal sealed class RecordingLeagueRewardGranter : ILeagueRewardGranter
    {
        public readonly List<string> GrantedIds = new List<string>();
        public readonly List<LeagueRewardPackage> GrantedPackages = new List<LeagueRewardPackage>();
        public bool Ready = true;

        public bool TryGrant(string grantId, LeagueRewardPackage package)
        {
            if (!Ready) return false;
            GrantedIds.Add(grantId);
            GrantedPackages.Add(package);
            return true;
        }
    }

    /// <summary>Decorator đếm lượt gọi — kiểm hệ thống không gửi thừa.</summary>
    internal sealed class CountingLeagueGroupService : ILeagueGroupService
    {
        private readonly ILeagueGroupService _inner;

        public CountingLeagueGroupService(ILeagueGroupService inner)
        {
            _inner = inner;
        }

        public const string AddTrophiesCall = "add";
        public const string GetGroupCall = "group";
        public const string GetPendingCall = "pending";

        public int AddTrophiesCallCount { get; private set; }
        public int GetGroupCallCount { get; private set; }
        public string LocalPlayerId => _inner.LocalPlayerId;

        /// <summary>Thứ tự các lượt gọi, dạng "loại:mùa" (mùa của grant với add, mùa theo đồng hồ với group / pending).</summary>
        public readonly List<string> CallLog = new List<string>();

        /// <summary>
        /// Chạy sau khi dịch vụ bọc trong đã dựng xong bảng nhưng TRƯỚC khi bảng tới tay nơi gọi — giả lập việc xảy ra trong lúc chờ
        /// mạng (vd đồng hồ vượt mốc đổi mùa). Null = không làm gì.
        /// </summary>
        public Action WhileGetGroupInFlight;

        /// <summary>
        /// Cổng phản hồi của GetGroupAsync: đặt thì dịch vụ bọc trong xử lý lượt gọi NGAY (yêu cầu đã tới server) nhưng phản hồi chỉ tới
        /// tay nơi gọi khi cổng mở — giả lập lượt tải đang bay. Mỗi lượt giữ cổng đang đặt lúc nó bắt đầu. Token của lượt gọi bị huỷ
        /// trong lúc chờ thì lượt gọi kết thúc bằng huỷ (phản hồi mất, như gọi mạng thật). Null = trả ngay.
        /// </summary>
        public TaskCompletionSource<bool> GetGroupResponseGate;

        /// <summary>Như <see cref="GetGroupResponseGate"/>, cho AddTrophiesAsync.</summary>
        public TaskCompletionSource<bool> AddTrophiesResponseGate;

        /// <summary>
        /// Cổng YÊU CẦU của GetPendingResultAsync: đặt thì yêu cầu tới dịch vụ bọc trong muộn — dịch vụ chỉ xử lý (đọc mùa đang giữ, chọn
        /// kết quả chờ) SAU khi cổng mở, như yêu cầu gửi trước mốc đổi mùa mà server xử lý sau mốc. Khác
        /// <see cref="GetGroupResponseGate"/> (xử lý ngay, phản hồi tới muộn). Mỗi lượt giữ cổng đang đặt lúc nó bắt đầu. Null = xử lý ngay.
        /// </summary>
        public TaskCompletionSource<bool> GetPendingRequestGate;

        /// <summary>
        /// Cổng PHẢN HỒI của GetPendingResultAsync: dịch vụ xử lý bình thường (sau <see cref="GetPendingRequestGate"/> nếu có) nhưng
        /// kết quả chỉ tới tay nơi gọi khi cổng mở — giả lập backend nhiều kết nối, phản hồi về sau một lượt gửi cúp bắt đầu sau nó.
        /// Mỗi lượt giữ cổng đang đặt lúc nó bắt đầu. Null = trả ngay.
        /// </summary>
        public TaskCompletionSource<bool> GetPendingResponseGate;

        /// <summary>
        /// Lượt gọi đang chờ cổng phớt lờ token (SDK mạng không hỗ trợ huỷ): huỷ token không kết thúc lượt gọi, phản hồi vẫn tới khi cổng
        /// mở. Mỗi lượt giữ giá trị đang đặt lúc nó bắt đầu.
        /// </summary>
        public bool ResponseGateIgnoresCancellation;

        /// <summary>Token của lượt GetGroupAsync gần nhất — kiểm lượt gọi dịch vụ có bị huỷ hay không.</summary>
        public CancellationToken LastGetGroupCancellationToken { get; private set; }

        public Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            GetGroupCallCount++;
            CallLog.Add(GetGroupCall + ":" + currentSeason.SeasonId);
            LastGetGroupCancellationToken = cancellationToken;
            Task<LeagueGroupSnapshot> call = _inner.GetGroupAsync(currentSeason, cancellationToken);
            WhileGetGroupInFlight?.Invoke();
            TaskCompletionSource<bool> gate = GetGroupResponseGate;
            return gate != null ? DeliverAfterGateAsync(call, gate, GateToken(cancellationToken)) : call;
        }

        public Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken)
        {
            AddTrophiesCallCount++;
            CallLog.Add(AddTrophiesCall + ":" + grant.SeasonId);
            Task<LeagueGroupSnapshot> call = _inner.AddTrophiesAsync(currentSeason, grant, cancellationToken);
            TaskCompletionSource<bool> gate = AddTrophiesResponseGate;
            return gate != null ? DeliverAfterGateAsync(call, gate, GateToken(cancellationToken)) : call;
        }

        /// <summary>Token lượt gọi tôn trọng khi chờ cổng: <see cref="CancellationToken.None"/> khi cổng phớt lờ huỷ.</summary>
        private CancellationToken GateToken(CancellationToken cancellationToken)
        {
            return ResponseGateIgnoresCancellation ? CancellationToken.None : cancellationToken;
        }

        private static async Task<T> DeliverAfterGateAsync<T>(Task<T> call, TaskCompletionSource<bool> gate, CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                await Task.WhenAny(gate.Task, Task.Delay(Timeout.Infinite, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                await gate.Task;
            }
            return await call;
        }

        public Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            CallLog.Add(GetPendingCall + ":" + currentSeason.SeasonId);
            TaskCompletionSource<bool> requestGate = GetPendingRequestGate;
            TaskCompletionSource<bool> responseGate = GetPendingResponseGate;
            Task<SeasonResult> call = requestGate != null
                ? ProcessAfterGateAsync(requestGate, () => _inner.GetPendingResultAsync(currentSeason, cancellationToken))
                : _inner.GetPendingResultAsync(currentSeason, cancellationToken);
            return responseGate != null ? DeliverAfterGateAsync(call, responseGate, CancellationToken.None) : call;
        }

        private static async Task<T> ProcessAfterGateAsync<T>(TaskCompletionSource<bool> gate, Func<Task<T>> call)
        {
            await gate.Task;
            return await call();
        }

        public Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken)
        {
            return _inner.AcknowledgeResultAsync(seasonId, cancellationToken);
        }

        public Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken)
        {
            return _inner.ClaimSeasonRewardAsync(seasonId, cancellationToken);
        }
    }

    /// <summary>Nơi lưu trong RAM có đếm lượt ghi / xoá — kiểm đồng hồ không ghi mỗi lần đọc giờ.</summary>
    internal sealed class CountingLeagueTextStore : ILeagueTextStore
    {
        private readonly InMemoryLeagueTextStore _inner = new InMemoryLeagueTextStore();

        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }

        public bool TryRead(string key, out string value)
        {
            return _inner.TryRead(key, out value);
        }

        public void Write(string key, string value)
        {
            WriteCount++;
            _inner.Write(key, value);
        }

        public void Delete(string key)
        {
            DeleteCount++;
            _inner.Delete(key);
        }
    }

    /// <summary>Chạy luồng popup kết quả mùa như game: lấy kết quả chờ → xem xong → nhận rương, tới khi hết việc.</summary>
    internal static class LeagueSeasonFlowDriver
    {
        /// <summary>Quá số lượt này mà vẫn còn kết quả chờ = luồng lặp mãi (đúng lỗi kết quả trùng mùa của 0.2.0).</summary>
        public const int MaximumRounds = 8;

        public static List<SeasonResult> ProcessAllPendingResults(ILeagueGroupService service, Func<SeasonWindow> currentSeason)
        {
            var processed = new List<SeasonResult>();
            for (int round = 0; round < MaximumRounds; round++)
            {
                SeasonResult pending = service.GetPendingResultAsync(currentSeason(), CancellationToken.None).Result;
                if (pending == null) return processed;
                processed.Add(pending);
                service.AcknowledgeResultAsync(pending.SeasonId, CancellationToken.None).Wait();
                _ = service.ClaimSeasonRewardAsync(pending.SeasonId, CancellationToken.None).Result;
            }
            throw new InvalidOperationException("Luồng kết quả mùa không dừng sau " + MaximumRounds +
                                                " lượt: kết quả trùng mùa, hoặc xem / nhận không trúng bản đang chờ.");
        }

        /// <summary>Như trên nhưng đi qua <see cref="LeagueSystem"/> (có đẩy hàng chờ cúp trước mỗi lần hỏi, như game gọi).</summary>
        public static List<SeasonResult> ProcessAllPendingResults(LeagueSystem system)
        {
            var processed = new List<SeasonResult>();
            for (int round = 0; round < MaximumRounds; round++)
            {
                SeasonResult pending = system.GetPendingSeasonResultAsync(CancellationToken.None).Result;
                if (pending == null) return processed;
                processed.Add(pending);
                system.AcknowledgeSeasonResultAsync(pending.SeasonId, CancellationToken.None).Wait();
                _ = system.ClaimSeasonRewardAsync(pending.SeasonId, CancellationToken.None).Result;
            }
            throw new InvalidOperationException("Luồng kết quả mùa không dừng sau " + MaximumRounds + " lượt.");
        }
    }

    /// <summary>
    /// Chạy một đoạn test không có SynchronizationContext. Test có lượt gọi treo (cổng phản hồi, độ trễ) mà chờ đồng bộ bằng
    /// .Result / .Wait() thì continuation không được post về main thread của Editor (đang bị chính test chặn) — không có context thì
    /// continuation chạy ngay khi cổng mở hoặc trên thread pool.
    /// </summary>
    internal static class WithoutSynchronizationContext
    {
        public static void Run(Action body)
        {
            SynchronizationContext previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                body();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }
    }

    /// <summary>
    /// Ngược với <see cref="WithoutSynchronizationContext"/>: một SynchronizationContext một luồng giống main thread của Unity. Phần
    /// tiếp theo của await được post vào hàng đợi và chỉ chạy khi test bơm hàng đợi trên thread của test
    /// (<see cref="PumpUntilCompleted"/>) — Task hoàn tất trên thread pool KHÔNG kéo phần tiếp theo sang thread pool, trừ khi code bỏ
    /// context bằng <c>ConfigureAwait(false)</c>. Cài bằng <see cref="Install"/>, <c>Dispose</c> trả lại context cũ.
    /// </summary>
    internal sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        /// <summary>Hàng đợi rỗng, không còn việc giữ lại để thả mà Task vẫn chưa xong sau chừng này = test treo, báo lỗi thay vì chờ mãi.</summary>
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Nhịp hỏi lại khi rảnh. Code bỏ context (ConfigureAwait(false)) đẩy phần tiếp theo sang thread pool — nó có thể post về hoặc
        /// sinh việc giữ lại mới bất cứ lúc nào, nên vừa chờ post vừa hỏi lại việc giữ lại theo nhịp này.
        /// </summary>
        private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(10);

        private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> _queue =
            new BlockingCollection<KeyValuePair<SendOrPostCallback, object>>();

        private readonly SynchronizationContext _previous;
        private readonly int _threadId;

        private SingleThreadSynchronizationContext()
        {
            _previous = Current;
            _threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static SingleThreadSynchronizationContext Install()
        {
            var context = new SingleThreadSynchronizationContext();
            SetSynchronizationContext(context);
            return context;
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            _queue.Add(new KeyValuePair<SendOrPostCallback, object>(callback, state));
        }

        public override void Send(SendOrPostCallback callback, object state)
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId) throw new NotSupportedException("Send từ thread khác sẽ treo test.");
            callback(state);
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }

        /// <summary>
        /// Chạy việc đã post trên thread này tới khi <paramref name="task"/> xong. Hàng đợi rỗng mà Task chưa xong thì gọi
        /// <paramref name="releaseHeldWork"/> để thả một việc đang giữ (vd phản hồi mạng); nó trả false = không còn gì để thả.
        /// </summary>
        public void PumpUntilCompleted(Task task, Func<bool> releaseHeldWork)
        {
            DateTime idleSinceUtc = DateTime.UtcNow;
            while (true)
            {
                if (_queue.TryTake(out KeyValuePair<SendOrPostCallback, object> work, task.IsCompleted ? TimeSpan.Zero : IdlePollInterval))
                {
                    work.Key(work.Value);
                    idleSinceUtc = DateTime.UtcNow;
                    continue;
                }
                if (task.IsCompleted) return;
                if (releaseHeldWork != null && releaseHeldWork())
                {
                    idleSinceUtc = DateTime.UtcNow;
                    continue;
                }
                if (DateTime.UtcNow - idleSinceUtc > IdleTimeout)
                {
                    throw new TimeoutException("Task chưa xong mà không còn việc nào để chạy — test treo.");
                }
            }
        }

        public void Dispose()
        {
            SetSynchronizationContext(_previous);
        }
    }

    /// <summary>
    /// Ghi lại mọi lần một việc chỉ được làm trên main thread (đọc/ghi nơi lưu kiểu PlayerPrefs, đọc đồng hồ của host, gọi dịch vụ,
    /// bắn sự kiện cho UI) chạy trên thread khác thread tạo recorder — trong test, thread tạo recorder đóng vai main thread.
    /// </summary>
    internal sealed class ThreadAffinityRecorder
    {
        private readonly object _gate = new object();
        private readonly List<string> _violations = new List<string>();
        private readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        public bool IsOnMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public void Check(string operation)
        {
            if (IsOnMainThread) return;
            lock (_gate) _violations.Add(operation + " (thread " + Thread.CurrentThread.ManagedThreadId + ")");
        }

        public List<string> Violations
        {
            get
            {
                lock (_gate) return new List<string>(_violations);
            }
        }
    }

    /// <summary>Nơi lưu trong RAM đòi main thread như PlayerPrefs: lần đọc / ghi / xoá ngoài main thread được ghi vào recorder.</summary>
    internal sealed class MainThreadOnlyLeagueTextStore : ILeagueTextStore
    {
        private readonly InMemoryLeagueTextStore _inner = new InMemoryLeagueTextStore();
        private readonly ThreadAffinityRecorder _recorder;

        public MainThreadOnlyLeagueTextStore(ThreadAffinityRecorder recorder)
        {
            _recorder = recorder;
        }

        public bool TryRead(string key, out string value)
        {
            _recorder.Check("đọc nơi lưu " + key);
            return _inner.TryRead(key, out value);
        }

        public void Write(string key, string value)
        {
            _recorder.Check("ghi nơi lưu " + key);
            _inner.Write(key, value);
        }

        public void Delete(string key)
        {
            _recorder.Check("xoá nơi lưu " + key);
            _inner.Delete(key);
        }
    }

    /// <summary>Đồng hồ của host đòi main thread (vd dựa trên Time.realtimeSinceStartup): lần đọc ngoài main thread được ghi vào recorder.</summary>
    internal sealed class MainThreadOnlyLeagueClock : ILeagueClock
    {
        private readonly ILeagueClock _inner;
        private readonly ThreadAffinityRecorder _recorder;

        public MainThreadOnlyLeagueClock(ILeagueClock inner, ThreadAffinityRecorder recorder)
        {
            _inner = inner;
            _recorder = recorder;
        }

        public DateTime UtcNow
        {
            get
            {
                _recorder.Check("đọc đồng hồ");
                return _inner.UtcNow;
            }
        }
    }

    /// <summary>
    /// Dịch vụ nhóm kiểu backend thật: yêu cầu tới "server" ngay lúc gọi (dịch vụ bọc trong xử lý đồng bộ trên thread gọi, phải trả Task
    /// đã xong — độ trễ 0), nhưng Task trả về chỉ hoàn tất khi test thả phản hồi, và hoàn tất TRÊN THREAD POOL như SDK mạng. Lượt gọi
    /// bắt đầu ngoài main thread được ghi vào recorder.
    /// </summary>
    internal sealed class ThreadPoolCompletingLeagueGroupService : ILeagueGroupService
    {
        private readonly ILeagueGroupService _inner;
        private readonly ThreadAffinityRecorder _recorder;
        private readonly object _gate = new object();
        private readonly Queue<Action> _heldResponses = new Queue<Action>();
        private int _responsesReleasedOnThreadPool;

        public ThreadPoolCompletingLeagueGroupService(ILeagueGroupService inner, ThreadAffinityRecorder recorder)
        {
            _inner = inner;
            _recorder = recorder;
        }

        /// <summary>Chạy trên thread pool ngay trước khi mỗi phản hồi tới — vd đồng hồ trôi trong lúc chờ mạng. Null = không làm gì.</summary>
        public Action BeforeEachResponse;

        public int ResponsesReleasedOnThreadPool => Volatile.Read(ref _responsesReleasedOnThreadPool);

        public string LocalPlayerId => _inner.LocalPlayerId;

        public Task<LeagueGroupSnapshot> GetGroupAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            return Hold("GetGroupAsync", () => _inner.GetGroupAsync(currentSeason, cancellationToken));
        }

        public Task<LeagueGroupSnapshot> AddTrophiesAsync(SeasonWindow currentSeason, LeagueTrophyGrant grant, CancellationToken cancellationToken)
        {
            return Hold("AddTrophiesAsync", () => _inner.AddTrophiesAsync(currentSeason, grant, cancellationToken));
        }

        public Task<SeasonResult> GetPendingResultAsync(SeasonWindow currentSeason, CancellationToken cancellationToken)
        {
            return Hold("GetPendingResultAsync", () => _inner.GetPendingResultAsync(currentSeason, cancellationToken));
        }

        public Task AcknowledgeResultAsync(string seasonId, CancellationToken cancellationToken)
        {
            return Hold("AcknowledgeResultAsync", () => CompletedAsTrue(_inner.AcknowledgeResultAsync(seasonId, cancellationToken)));
        }

        public Task<LeagueRewardPackage> ClaimSeasonRewardAsync(string seasonId, CancellationToken cancellationToken)
        {
            return Hold("ClaimSeasonRewardAsync", () => _inner.ClaimSeasonRewardAsync(seasonId, cancellationToken));
        }

        /// <summary>
        /// Thả phản hồi giữ lâu nhất: hoàn tất Task của nó trên thread pool rồi chờ phần chạy đồng bộ theo sau xong. false = không còn
        /// phản hồi nào đang giữ.
        /// </summary>
        public bool TryReleaseNextResponseOnThreadPool()
        {
            Action release;
            lock (_gate)
            {
                if (_heldResponses.Count == 0) return false;
                release = _heldResponses.Dequeue();
            }

            ThreadPoolWork.RunAndWait(() =>
            {
                if (_recorder.IsOnMainThread) throw new InvalidOperationException("Phản hồi phải tới trên thread pool, không phải main thread.");
                BeforeEachResponse?.Invoke();
                release();
                Interlocked.Increment(ref _responsesReleasedOnThreadPool);
            });
            return true;
        }

        private Task<T> Hold<T>(string operation, Func<Task<T>> call)
        {
            _recorder.Check("gọi " + operation);
            Task<T> served;
            try
            {
                served = call();
            }
            catch (Exception exception)
            {
                served = Task.FromException<T>(exception);
            }
            if (!served.IsCompleted) throw new InvalidOperationException("Dịch vụ bọc trong phải trả Task đã xong (độ trễ 0).");

            var response = new TaskCompletionSource<T>();
            lock (_gate) _heldResponses.Enqueue(() => CopyOutcome(served, response));
            return response.Task;
        }

        private static void CopyOutcome<T>(Task<T> served, TaskCompletionSource<T> response)
        {
            if (served.IsCanceled) response.SetCanceled();
            else if (served.IsFaulted) response.SetException(served.Exception.InnerExceptions);
            else response.SetResult(served.Result);
        }

        private static Task<bool> CompletedAsTrue(Task task)
        {
            return task.ContinueWith(completed =>
            {
                completed.GetAwaiter().GetResult();
                return true;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Chạy một việc trên thread pool (không phải Task.Run: chờ bằng Task.Wait có thể chạy luôn việc đó trên thread đang chờ) rồi chờ
    /// nó xong. Dùng để thả phản hồi mạng hoặc huỷ token từ thread nền như timer của <c>CancelAfter</c>.
    /// </summary>
    internal static class ThreadPoolWork
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

        public static void RunAndWait(Action work)
        {
            Exception failure = null;
            // Không Dispose: quá giờ mà việc vẫn chạy thì nó còn Set sự kiện này; Set trên sự kiện đã Dispose sẽ ném trên thread pool.
            var done = new ManualResetEventSlim(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    done.Set();
                }
            });
            if (!done.Wait(WaitTimeout)) throw new TimeoutException("Việc trên thread pool không xong.");
            if (failure != null) throw new InvalidOperationException("Việc trên thread pool hỏng.", failure);
        }
    }
}
