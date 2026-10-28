namespace NX_lims_Softlines_Command_System.src.Application.Contract.Realtime
{
    /// <summary>
    /// 业务链路进度快照的信封。
    ///
    /// 设计意图：把「业务怎么看进度」与「推送机制怎么发」解耦。
    /// 推送枢纽只把它当作**不透明负载**转发给前端，不认识其中任何业务字段；
    /// 参与哈希计算的字段由各链路自己在 <see cref="IProgressSnapshotProvider.ComputeHash"/> 中决定。
    /// </summary>
    public sealed class ProgressSnapshot
    {
        /// <summary>业务主键（如 checkListId 的字符串形式）。</summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// 链路自身的总体状态字符串。
        /// 仅供 Provider 内部判定（如 IsCompleted）与日志诊断，推送枢纽**不解释**它的取值含义。
        /// </summary>
        public string Status { get; init; } = string.Empty;

        /// <summary>
        /// 参与哈希计算的业务字段 → 值。
        /// 该字典只用于 Provider 自己计算哈希，不参与前端下发；
        /// 作用是让快照自描述「哪些字段影响变化」，便于 Provider 逐字复刻既有哈希口径。
        /// </summary>
        public IReadOnlyDictionary<string, string> Fingerprint { get; init; }
            = new Dictionary<string, string>();

        /// <summary>
        /// 原样下发给前端的业务负载（直接放既有 DTO 实例，保证前端 JSON 结构零变更）。
        /// </summary>
        public object Payload { get; init; } = new object();

        /// <summary>快照生成时间，用于日志与排查。</summary>
        public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
