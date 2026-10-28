using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse
{
    /// <summary>
    /// <see cref="ISseClient"/> 的 HTTP 响应适配实现：把事件写到当前请求的 <c>HttpResponse</c>。
    ///
    /// 设计取舍：本类必须绑定一个具体的 <c>HttpResponse</c> 实例，因此**不能**注册为单例，
    /// 由 Controller 在建立流时直接 <c>new</c> 出来。
    /// </summary>
    public sealed class HttpResponseSseClient : ISseClient
    {
        private readonly HttpResponse _response;
        private readonly ILogger<HttpResponseSseClient> _logger;

        /// <summary>
        /// 串行化写入：同一响应的 Body 被并发写会产生交错的字节流，前端会解析失败。
        /// 一次只允许一个写入者进入临界区。
        /// </summary>
        private readonly SemaphoreSlim _lock = new(1, 1);

        private volatile bool _closed;

        /// <param name="response">当前请求的响应对象。</param>
        /// <param name="logger">日志。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        public HttpResponseSseClient(HttpResponse response, ILogger<HttpResponseSseClient> logger)
        {
            _response = response ?? throw new ArgumentNullException(nameof(response));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Id = Guid.NewGuid().ToString("N");
        }

        /// <inheritdoc />
        public string Id { get; }

        /// <inheritdoc />
        public bool IsClosed => _closed;

        /// <inheritdoc />
        public async Task SendAsync(string eventName, object payload, CancellationToken ct = default)
        {
            // 已关闭的连接不必再尝试写入：客户端大概率已断开，
            // 继续写只会不断抛异常，让调用方的 dead 清理逻辑反复空转。
            if (_closed) return;

            await _lock.WaitAsync(ct);
            try
            {
                if (_closed) return;

                var json = JsonSerializer.Serialize(payload);

                // SSE 规范：data 字段的值若含换行，必须逐行拆成多条 "data: "，
                // 否则前端 EventSource 会把换行前的内容当成一个完整事件、后续内容丢弃。
                // 原实现直接写单行 data，对含换行的 JSON 是隐患，这里补上。
                var lines = json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                await _response.WriteAsync($"event: {eventName}\n", ct);
                foreach (var line in lines)
                {
                    await _response.WriteAsync($"data: {line}\n", ct);
                }
                await _response.WriteAsync("\n", ct);

                await _response.Body.FlushAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task CompleteAsync(CancellationToken ct = default)
        {
            if (_closed) return;
            _closed = true;

            try
            {
                // 只做「尽力刷出」。真正的连接终止由请求管道在 action 返回时完成
                // （HttpResponse 没有公开的 Complete API），详见接口注释。
                await _response.Body.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                // 客户端已断开时刷出必然失败，这属正常路径而非故障，降级为 Debug 避免日志噪音。
                _logger.LogDebug(ex, "SSE 连接 {ClientId} 关闭时刷出失败（客户端可能已断开），已忽略。", Id);
            }
        }
    }
}
