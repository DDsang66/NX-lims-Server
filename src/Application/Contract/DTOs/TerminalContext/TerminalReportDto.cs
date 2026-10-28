using System.Text.Json;

namespace NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TerminalContext
{
    /// <summary>
    /// 终端上报载荷。
    ///
    /// 唯一职责是「把终端侧的串口交互原样搬到后端」，因此 <see cref="Payload"/>
    /// 刻意保持为 <see cref="JsonElement"/> —— 后端不解析、不改结构，保证监听端看到的
    /// 字段名与设备输出完全一致（监听端要按这些字段名自行组装下游业务数据）。
    /// </summary>
    public sealed class TerminalReportDto
    {
        /// <summary>
        /// 终端标识。为空时由后端按请求来源 IP 推断。
        /// 当前可行性验证阶段前端无需传，交给后端取 IP 更不容易被伪造错误值。
        /// </summary>
        public string? TerminalId { get; init; }

        /// <summary>
        /// 终端侧采集到该报文的时间。为空时后端用接收时间代替。
        /// 保留该字段是为了让监听端能区分「设备产生时间」与「后端收到时间」。
        /// </summary>
        public DateTimeOffset? Timestamp { get; init; }

        /// <summary>
        /// 串口交互的原始 JSON，原样透传给监听端。
        /// 后端**禁止**做任何结构化或字段重命名，否则监听端的字段映射会随之失效。
        /// </summary>
        public JsonElement Payload { get; init; }
    }
}
