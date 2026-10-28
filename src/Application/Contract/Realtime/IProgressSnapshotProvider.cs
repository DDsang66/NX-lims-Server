namespace NX_lims_Softlines_Command_System.src.Application.Contract.Realtime
{
    /// <summary>
    /// 一条业务链路的进度快照提供者。
    ///
    /// 这是「接入第二条链路」的**唯一扩展点**：新增链路只需实现本接口并标注 <c>ISingletonDependency</c>，
    /// 通用轮询服务会自动发现并纳入派发，无需修改推送枢纽、轮询服务或任何注册代码。
    ///
    /// 生命周期约定：实现类应为 **Singleton**（无状态），在需要 Scoped 依赖时
    /// 由实现内部通过 <c>IServiceScopeFactory</c> 自建 scope 获取，避免 captive dependency。
    /// </summary>
    public interface IProgressSnapshotProvider
    {
        /// <summary>
        /// 频道名（如 "datasheet"）。推送枢纽用它隔离不同业务链路的连接，
        /// 因此必须全局唯一且稳定，不得随请求变化。
        /// </summary>
        string Channel { get; }

        /// <summary>
        /// 取当前**存在活跃订阅**的业务 key 集合。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>活跃 key 集合；无订阅时返回空集合。</returns>
        /// <remarks>
        /// 「无订阅返回空集合」是整套机制零开销的前提：轮询服务据此跳过取快照，
        /// 从而做到「没人看的时候不查库」。实现不得返回全量业务 key。
        /// </remarks>
        Task<IReadOnlyCollection<string>> GetActiveKeysAsync(CancellationToken ct);

        /// <summary>
        /// 取指定 key 的进度快照。
        /// </summary>
        /// <param name="key">业务主键的字符串形式。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>快照；若该 key 已不存在，应抛异常或返回 Status 表示终态的快照（由实现决定，需在 AGENTS.md 记录）。</returns>
        Task<ProgressSnapshot> GetSnapshotAsync(string key, CancellationToken ct);

        /// <summary>
        /// 判定该快照是否已进入终态（终态后枢纽会推 completed 并关闭连接）。
        /// </summary>
        /// <param name="snapshot">待判定的快照。</param>
        /// <returns>true 表示已终态，不再需要继续推送。</returns>
        /// <remarks>
        /// 注意：同一业务链路内必须与前端约定的终态口径一致，
        /// 否则会出现「后端认为已完成并关流，前端仍在等待」的错位。
        /// </remarks>
        bool IsCompleted(ProgressSnapshot snapshot);

        /// <summary>
        /// 计算快照指纹哈希。
        /// </summary>
        /// <param name="snapshot">待计算哈希的快照。</param>
        /// <returns>哈希字符串；推送枢纽只做相等比较，不解释其内容。</returns>
        /// <remarks>
        /// 由业务侧实现的原因：哈希必须覆盖「该链路所有会影响界面显示的字段」，
        /// 而只有链路自己知道是哪些字段。枢纽若硬编码业务字段，通用化即失效。
        /// 实现时必须保证：字段拼接顺序、分隔符、时间精度**逐字稳定**，
        /// 否则同一份数据会算出不同哈希，导致无意义的重复推送。
        /// </remarks>
        string ComputeHash(ProgressSnapshot snapshot);
    }
}
