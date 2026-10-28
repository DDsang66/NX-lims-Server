using NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse;

namespace NX_lims_Softlines_Command_System.src.Application.Service.Realtime
{
    /// <summary>
    /// 链路无关的 SSE 推送枢纽。
    ///
    /// 与原 <c>DataSheetProgressHub</c> 的差别：
    /// · 连接以 <c>(channel, key)</c> 两段定位，而非单一 <c>Guid checkListId</c>；
    /// · 不再包含任何业务字段的哈希计算——哈希由各链路 Provider 提供。
    ///
    /// 保持不变的关键设计（改造时不得破坏）：
    /// · 哈希去重：数据没变不推；
    /// · 只对活跃订阅者派发：无订阅时调用方不会取快照（零 DB 开销的前提）；
    /// · dead 连接反注册：写失败或已关闭的连接会被自动清理；
    /// · 终态关流：调用方在终态时调 <see cref="CloseAllAsync"/>。
    /// </summary>
    public interface ISseHub
    {
        /// <summary>
        /// 注册一个订阅连接。
        /// </summary>
        /// <param name="channel">业务频道名（如 "datasheet"）。</param>
        /// <param name="key">业务主键（如 checkListId 的字符串形式）。</param>
        /// <param name="client">SSE 连接。</param>
        /// <param name="ct">取消令牌。</param>
        /// <exception cref="ArgumentException">channel 或 key 为空白。</exception>
        /// <exception cref="ArgumentNullException">client 为 null。</exception>
        Task RegisterAsync(string channel, string key, ISseClient client, CancellationToken ct = default);

        /// <summary>
        /// 取消注册（连接正常结束或已被判定为 dead 时调用）。
        /// </summary>
        /// <param name="channel">业务频道名。</param>
        /// <param name="key">业务主键。</param>
        /// <param name="client">要移除的连接。</param>
        /// <returns>移除完成的任务；key 不存在时为无操作。</returns>
        Task UnregisterAsync(string channel, string key, ISseClient client);

        /// <summary>
        /// 取指定频道下当前**有活跃连接**的业务 key 集合。
        /// </summary>
        /// <param name="channel">业务频道名。</param>
        /// <returns>key 集合；无活跃连接时返回空集合（不含创建过但已清空的 key）。</returns>
        IReadOnlyCollection<string> GetActiveKeys(string channel);

        /// <summary>
        /// 比对并更新该 key 的最近一次快照哈希。
        /// </summary>
        /// <param name="channel">业务频道名。</param>
        /// <param name="key">业务主键。</param>
        /// <param name="hash">本次快照的哈希。</param>
        /// <returns>true 表示与上次不同（应推送）；false 表示未变化（应跳过推送）。</returns>
        bool HasChanged(string channel, string key, string hash);

        /// <summary>
        /// 向该 key 下的所有连接广播一个事件。
        /// </summary>
        /// <param name="channel">业务频道名。</param>
        /// <param name="key">业务主键。</param>
        /// <param name="eventName">事件名（progress / completed 等）。</param>
        /// <param name="payload">负载，原样下发。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>广播完成的任务；单个连接失败不会中断其它连接。</returns>
        Task BroadcastAsync(string channel, string key, string eventName, object payload, CancellationToken ct = default);

        /// <summary>
        /// 关闭该 key 下的所有连接并清理哈希记录。
        /// </summary>
        /// <param name="channel">业务频道名。</param>
        /// <param name="key">业务主键。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>关闭完成的任务。</returns>
        Task CloseAllAsync(string channel, string key, CancellationToken ct = default);
    }
}
