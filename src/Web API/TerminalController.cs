using Microsoft.AspNetCore.Mvc;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TerminalContext;
using NX_lims_Softlines_Command_System.src.Application.Service.Realtime;
using NX_lims_Softlines_Command_System.src.Application.Service.TerminalContext;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse;

namespace NX_lims_Softlines_Command_System.src.Web_API
{
    /// <summary>
    /// 串口测试实时监听链路的 HTTP 入口。
    ///
    /// 链路：终端 A 上报串口原始 JSON → 本控制器转发 → 通过 SSE 推给所有监听连接（终端 B）。
    ///
    /// 职责边界：后端**只做转发**，不解析、不落库、不做业务加工；
    /// 监听端收到后自行组装并提交到下游业务接口，与本控制器无关。
    /// </summary>
    [ApiController]
    [Route("api/[Controller]")]
    public class TerminalController : ControllerBase
    {
        private readonly ITerminalReportService _terminalService;
        private readonly ISseHub _hub;
        private readonly ILogger<HttpResponseSseClient> _sseLogger;

        /// <summary>
        /// 构造终端监听控制器。
        /// </summary>
        /// <param name="terminalService">上报处理服务。</param>
        /// <param name="hub">通用推送枢纽（本链路只复用，不新增推送设施）。</param>
        /// <param name="sseLogger">SSE 客户端日志。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        public TerminalController(
            ITerminalReportService terminalService,
            ISseHub hub,
            ILogger<HttpResponseSseClient> sseLogger)
        {
            _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _sseLogger = sseLogger ?? throw new ArgumentNullException(nameof(sseLogger));
        }

        /// <summary>
        /// 终端 A 上报一次串口交互数据。
        /// </summary>
        /// <param name="dto">上报载荷；<c>Payload</c> 为串口原始 JSON，原样透传。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>
        /// <c>Result.Ok</c> 表示已转发；<c>Result.Fail</c> 携带可读原因
        /// （载荷为空 / 缺 payload / 无法确定终端标识）。
        /// </returns>
        /// <remarks>
        /// 终端标识默认由来源 IP 推断，前端无需传 <c>TerminalId</c>。
        /// 当前无鉴权：任何能访问本地址的机器都可上报，风险已在 AGENTS.md 登记。
        /// </remarks>
        [HttpPost("report")]
        public async Task<Result> Report([FromBody] TerminalReportDto dto, CancellationToken ct)
            => await _terminalService.ReportAsync(dto, ResolveClientIp(), ct);

        /// <summary>
        /// 监听端订阅实时数据流（SSE）。
        /// </summary>
        /// <param name="ct">取消令牌；客户端断开时触发。</param>
        /// <returns>保持连接直到客户端断开，本身不返回业务数据。</returns>
        /// <remarks>
        /// 采用「单连接订阅全部」方案：所有监听连接都注册到同一个固定 key，
        /// 由前端按 <c>message.terminalId</c> 过滤显示。这样增删监听终端**不需要重连**，
        /// 也不会在重连窗口内丢事件（串口数据没有补发机制）。
        ///
        /// 事件序列：
        /// 1. <c>ready</c> —— 连接建立后立即下发，携带当前活跃终端列表；
        /// 2. <c>message</c> —— 每次终端上报各推一次，载荷含 <c>terminalId</c> / <c>timestamp</c> / <c>payload</c>；
        /// 3. <c>heartbeat</c> —— 每 15 秒一次，仅用于前端判断连接存活。
        /// </remarks>
        [HttpGet("stream")]
        public async Task Stream(CancellationToken ct)
        {
            // header 配置与 SSE 客户端都复用 Infrastructure\Realtime\Sse，不在控制器里手抄。
            SseResponseConfigurator.Configure(Response);

            var client = new HttpResponseSseClient(Response, _sseLogger);
            await _hub.RegisterAsync(TerminalReportService.Channel, TerminalReportService.BroadcastKey, client, ct);

            try
            {
                // 首帧告知「已就绪 + 当前有哪些终端可监」，前端据此初始化勾选面板。
                // 注意：浏览器自动重连后会再次收到该事件，前端需按幂等方式重建列表。
                await client.SendAsync(TerminalReportService.ReadyEvent, new
                {
                    terminals = _terminalService.GetActiveTerminals(),
                    serverTime = DateTimeOffset.UtcNow
                }, ct);

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TerminalReportService.HeartbeatInterval, ct);

                    await client.SendAsync(TerminalReportService.HeartbeatEvent, new
                    {
                        ts = DateTimeOffset.UtcNow
                    }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开属正常结束路径，不记错误日志。
            }
            finally
            {
                await _hub.UnregisterAsync(TerminalReportService.Channel, TerminalReportService.BroadcastKey, client);
            }
        }

        /// <summary>
        /// 解析请求来源 IP，作为缺省终端标识。
        /// </summary>
        /// <returns>规范化后的 IPv4 字符串；无法解析时返回 null。</returns>
        /// <remarks>
        /// 双栈监听时 <c>RemoteIpAddress</c> 可能是 <c>::ffff:10.0.0.5</c> 形式，
        /// 若不做 IPv4 映射规范化，同一台机器会与上报端得出两个不同 key，
        /// 表现为「明明上报了、监听端却收不到」且没有任何报错。
        /// </remarks>
        private string? ResolveClientIp()
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            if (ip == null) return null;

            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

            return ip.ToString();
        }
    }
}
