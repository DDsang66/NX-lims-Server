using NX_lims_Softlines_Command_System.src.Application.Contract.Realtime;

namespace NX_lims_Softlines_Command_System.src.Application.Service.Realtime
{
    /// <summary>
    /// 通用进度轮询派发服务：每秒一轮，遍历所有已注册的 <see cref="IProgressSnapshotProvider"/>，
    /// 只对**有活跃订阅**的业务 key 取快照、算哈希、按需广播。
    ///
    /// 本类取代原 <c>DataSheetProgressPollingService</c> 中硬编码 DataSheet 快照的轮询逻辑，
    /// 使得新增业务链路不需要再写第二个轮询服务。
    ///
    /// 保留的关键设计：
    /// · 1 秒周期（与原实现一致）；
    /// · 只对有活跃连接的 key 取快照 → 无人订阅时不查库；
    /// · 哈希不变则不广播；
    /// · 终态时推 completed 并关闭连接；
    /// · 单轮异常不杀死后台循环。
    /// </summary>
    public class ProgressPollingService : BackgroundService
    {
        /// <summary>常规进度事件名。前端按此名监听，属对外契约，不可随意改动。</summary>
        private const string ProgressEvent = "progress";

        /// <summary>终态事件名。前端收到后应主动关闭 EventSource。</summary>
        private const string CompletedEvent = "completed";

        /// <summary>轮询间隔：与原实现保持一致（1 秒）。</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly ISseHub _hub;
        private readonly IEnumerable<IProgressSnapshotProvider> _providers;
        private readonly ILogger<ProgressPollingService> _logger;

        /// <summary>
        /// 构造通用轮询派发服务。
        /// </summary>
        /// <param name="hub">推送枢纽。</param>
        /// <param name="providers">
        /// 所有已注册的链路提供者。由 DI 按 <see cref="IProgressSnapshotProvider"/> 自动收集，
        /// 因此新增链路时本类与 DI 注册都无需改动。
        /// </param>
        /// <param name="logger">日志。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        public ProgressPollingService(
            ISseHub hub,
            IEnumerable<IProgressSnapshotProvider> providers,
            ILogger<ProgressPollingService> logger)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 后台轮询主循环。
        /// </summary>
        /// <param name="ct">应用停止时触发。</param>
        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            // 启动时把已注册的频道打出来：新增链路接入后，看这一行就能确认是否被自动纳入。
            var channels = _providers.Select(p => p.Channel).ToList();
            _logger.LogInformation(
                "ProgressPollingService 已启动，注册的进度链路：{Channels}",
                channels.Count > 0 ? string.Join(", ", channels) : "（无）");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    foreach (var provider in _providers)
                    {
                        await PollOneProviderAsync(provider, ct);
                    }
                }
                catch (Exception ex)
                {
                    // 单轮失败不能让后台循环退出，否则整条推送链路静默停摆。
                    _logger.LogError(ex, "ProgressPollingService 轮询异常，将在下一轮重试。");
                }

                // 取消时 Task.Delay 会抛 OperationCanceledException，BackgroundService 视为正常停止。
                await Task.Delay(PollInterval, ct);
            }
        }

        /// <summary>
        /// 轮询单个链路：取活跃 key → 逐个取快照 → 哈希比对 → 广播 → 终态关流。
        /// </summary>
        private async Task PollOneProviderAsync(IProgressSnapshotProvider provider, CancellationToken ct)
        {
            IReadOnlyCollection<string> activeKeys;

            try
            {
                activeKeys = await provider.GetActiveKeysAsync(ct);
            }
            catch (Exception ex)
            {
                // 单个 Provider 故障不应影响其它链路
                _logger.LogError(ex, "获取活跃 key 失败，已跳过本轮。Channel={Channel}", provider.Channel);
                return;
            }

            foreach (var key in activeKeys)
            {
                try
                {
                    var snapshot = await provider.GetSnapshotAsync(key, ct);
                    if (snapshot == null) continue;

                    var hash = provider.ComputeHash(snapshot);
                    if (!_hub.HasChanged(provider.Channel, key, hash)) continue;

                    await _hub.BroadcastAsync(provider.Channel, key, ProgressEvent, snapshot.Payload, ct);

                    if (provider.IsCompleted(snapshot))
                    {
                        await _hub.BroadcastAsync(provider.Channel, key, CompletedEvent, snapshot.Payload, ct);
                        await _hub.CloseAllAsync(provider.Channel, key, ct);
                    }
                }
                catch (Exception ex)
                {
                    // 单个 key 失败不应拖垮同频道的其它 key
                    _logger.LogError(ex,
                        "推送进度失败，已跳过该 key。Channel={Channel}，Key={Key}",
                        provider.Channel, key);
                }
            }
        }
    }
}
