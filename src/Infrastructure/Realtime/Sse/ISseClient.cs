namespace NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse
{
    /// <summary>
    /// SSE 客户端连接的抽象（出站端口）。
    ///
    /// 存在意义：把「怎么把字节写到 HTTP 响应」这件技术细节与上层隔离。
    /// 原实现 <c>SseEmitter</c> 直接持有 <c>HttpResponse</c>（ASP.NET Core 类型）却被放在 Domain 层，
    /// 导致领域层被 Web 框架绑架；本接口承担同样的职责，但落在 Infrastructure 层。
    /// </summary>
    public interface ISseClient
    {
        /// <summary>连接唯一标识，用于日志诊断与 dead 连接追踪。</summary>
        string Id { get; }

        /// <summary>是否已关闭。关闭后 <see cref="SendAsync"/> 应直接返回而不抛异常。</summary>
        bool IsClosed { get; }

        /// <summary>
        /// 发送一个 SSE 事件（<c>event:</c> + 一行或多行 <c>data:</c> + 空行）。
        /// </summary>
        /// <param name="eventName">事件名（如 progress / completed / heartbeat）。</param>
        /// <param name="payload">任意可序列化负载，序列化后按 SSE 规范逐行加 <c>data: </c> 前缀。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>写入并刷出完成的任务。</returns>
        /// <exception cref="ObjectDisposedException">底层响应流已释放（调用方应捕获并把该连接视为 dead）。</exception>
        Task SendAsync(string eventName, object payload, CancellationToken ct = default);

        /// <summary>
        /// 优雅结束该连接：置 <see cref="IsClosed"/> 并尽力把缓冲区刷出。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>关闭动作完成的任务。</returns>
        /// <remarks>
        /// 注意：<c>HttpResponse</c> 不提供公开的「Complete」API，真正的连接终止由请求管道在
        /// action 返回时完成。因此本方法只能做到「置状态 + 尽力 flush」，不要期望它立即断开 TCP。
        /// 若将来需要强制断开，应改为持有 <c>HttpContext</c> 并调用 <c>Abort()</c>。
        /// </remarks>
        Task CompleteAsync(CancellationToken ct = default);
    }
}
