using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TerminalContext;
using NX_lims_Softlines_Command_System.src.Domain.Share;

namespace NX_lims_Softlines_Command_System.src.Application.Service.TerminalContext
{
    /// <summary>
    /// 串口测试实时监听链路的上报服务。
    ///
    /// 后端在本链路中的职责**只有转发**：把终端 A 上报的串口原始 JSON 原样推给所有监听连接，
    /// 不解析、不落库、不做业务加工（监听端收到后自行组装提交）。
    /// </summary>
    public interface ITerminalReportService
    {
        /// <summary>
        /// 处理一次终端上报：推导终端标识 → 记录活跃状态 → 广播给所有监听连接。
        /// </summary>
        /// <param name="dto">上报载荷；<see cref="TerminalReportDto.Payload"/> 必填。</param>
        /// <param name="sourceIp">请求来源 IP（由 Controller 解析并已做 IPv4 映射规范化）；可为 null。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>
        /// 成功返回 <c>Result.Ok</c>；以下情形返回 <c>Result.Fail</c>（带可读原因，不静默丢弃）：
        /// 载荷为 null、<c>Payload</c> 缺失、无法确定终端标识。
        /// </returns>
        Task<Result> ReportAsync(TerminalReportDto dto, string? sourceIp, CancellationToken ct = default);

        /// <summary>
        /// 取最近活跃的终端标识列表（默认 5 分钟内有过上报即视为在线）。
        /// SSE 首帧的 <c>ready</c> 事件用它告知监听端「当前有哪些终端可监」。
        /// </summary>
        /// <returns>按序数排序的终端标识列表；无活跃终端时返回空列表。</returns>
        IReadOnlyList<string> GetActiveTerminals();
    }
}
