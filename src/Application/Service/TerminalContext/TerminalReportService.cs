using System.Collections.Concurrent;
using System.Text.Json;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TerminalContext;
using NX_lims_Softlines_Command_System.src.Application.Service.Realtime;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Application.Service.TerminalContext
{
    /// <summary>
    /// <see cref="ITerminalReportService"/> 的实现。
    ///
    /// 生命周期为 **Singleton**：需要跨请求维护「哪些终端最近上报过」这份进程内状态。
    ///
    /// 与 DataSheet 进度推送的关键差异：本链路是**事件流**而非状态快照，因此
    /// · 直接调用 <see cref="ISseHub.BroadcastAsync"/>，每条上报都要推；
    /// · **不使用** <c>IProgressSnapshotProvider</c> / <c>ProgressPollingService</c>（没有「快照」概念）；
    /// · **不使用** <see cref="ISseHub.HasChanged"/> 的哈希去重 —— 设备可能连发两条完全相同的报文，
    ///   去重会让第二条直接消失，而监听端本应看到两条。
    /// </summary>
    public class TerminalReportService : ITerminalReportService, ISingletonDependency
    {
        /// <summary>串口监听链路的频道名。</summary>
        public const string Channel = "terminal";

        /// <summary>
        /// 广播用的固定 key。
        /// 监听端采用「单连接订阅全部」方案（动态增删在前端过滤），因此所有监听连接都注册在这一个 key 下，
        /// 不做 per-terminal 的精准派发 —— 换来做「增删监听终端不需要重连、也不会丢帧」。
        /// </summary>
        public const string BroadcastKey = "all";

        /// <summary>上报事件名。前端需用 <c>addEventListener('message', ...)</c> 监听。</summary>
        public const string MessageEvent = "message";

        /// <summary>首帧就绪事件名，携带当前活跃终端列表。</summary>
        public const string ReadyEvent = "ready";

        /// <summary>心跳事件名，仅用于让前端判断连接存活。</summary>
        public const string HeartbeatEvent = "heartbeat";

        /// <summary>心跳间隔。与 <c>DataSheetController</c> 的 SSE 端点保持一致，用于顶住中间层空闲超时。</summary>
        public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        /// <summary>活跃终端的判定窗口：超过该时长没有上报即视为离线。</summary>
        public static readonly TimeSpan ActiveTerminalTtl = TimeSpan.FromMinutes(5);

        private readonly ISseHub _hub;
        private readonly ILogger<TerminalReportService> _logger;

        /// <summary>
        /// 终端标识 → 最近一次上报时间。
        ///
        /// 用进程内字典而不是 <c>IDistributedCache</c>：后者不支持枚举 key，
        /// 拿不到「当前有哪些活跃终端」，而 <c>ready</c> 首帧正需要这份列表。
        /// 当前为单实例部署，进程内状态足够；多副本时的处置见本目录 AGENTS.md。
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen
            = new(StringComparer.Ordinal);

        /// <summary>
        /// 构造上报服务。
        /// </summary>
        /// <param name="hub">推送枢纽（复用通用实现，本链路不新增任何推送设施）。</param>
        /// <param name="logger">日志。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        public TerminalReportService(ISseHub hub, ILogger<TerminalReportService> logger)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<Result> ReportAsync(TerminalReportDto dto, string? sourceIp, CancellationToken ct = default)
        {
            if (dto == null) return Result.Fail("上报内容不能为空");

            // TODO(鉴权) 当前上报与订阅均匿名：任何能访问该地址的机器都可伪造上报、也可订阅任意终端数据。
            //          待 user 聚合根重构完成后统一接入鉴权，届时在此校验调用方身份。

            // Payload 缺失时直接拒绝：JsonElement 默认是 Undefined，其序列化会抛 InvalidOperationException，
            // 与其让异常冒到全局处理器，不如在这里给出可读原因。
            if (dto.Payload.ValueKind == JsonValueKind.Undefined)
                return Result.Fail("上报内容缺少 payload（串口原始 JSON）");

            var terminalId = !string.IsNullOrWhiteSpace(dto.TerminalId)
                ? dto.TerminalId!.Trim()
                : sourceIp;

            if (string.IsNullOrWhiteSpace(terminalId))
                return Result.Fail("无法确定终端标识：请求未携带 TerminalId，且无法解析来源 IP");

            var receivedAt = DateTimeOffset.UtcNow;

            // 先记活跃再广播：即使广播因无人订阅而无实际接收方，该终端的「在线」状态也应成立。
            _lastSeen[terminalId] = receivedAt;

            // Payload 原样放进匿名对象：不序列化再解析，避免字段名或数值精度被改变。
            await _hub.BroadcastAsync(Channel, BroadcastKey, MessageEvent, new
            {
                terminalId,
                timestamp = dto.Timestamp ?? receivedAt,
                payload = dto.Payload
            }, ct);

            _logger.LogDebug(
                "终端上报已转发。TerminalId={TerminalId}，当前活跃终端数={ActiveCount}",
                terminalId, _lastSeen.Count);

            return Result.Ok();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetActiveTerminals()
        {
            var threshold = DateTimeOffset.UtcNow - ActiveTerminalTtl;
            var active = new List<string>();

            foreach (var pair in _lastSeen)
            {
                if (pair.Value < threshold)
                {
                    // 懒清理：本服务没有后台回收任务，在读取时顺手摘除过期项，
                    // 防止字典随「只上报过一次的历史终端」无限增长。
                    _lastSeen.TryRemove(pair.Key, out _);
                    continue;
                }

                active.Add(pair.Key);
            }

            // 排序保证同一时刻多次调用返回顺序一致，便于前端 diff 时避免无意义的列表抖动。
            active.Sort(StringComparer.Ordinal);
            return active;
        }
    }
}
