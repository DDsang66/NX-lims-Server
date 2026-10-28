using Microsoft.AspNetCore.Http;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse
{
    /// <summary>
    /// SSE 响应头统一配置器。
    ///
    /// 存在意义：这三个 header 是 SSE 能正常工作的技术前提，原先手写在
    /// <c>DataSheetController.StreamProgress</c> 里；第二条链路接入时若各自手抄一遍，
    /// 极易漏掉 <c>X-Accel-Buffering</c> 而导致「本地正常、线上不刷新」。
    /// </summary>
    public static class SseResponseConfigurator
    {
        /// <summary>
        /// 为响应设置 SSE 所需的头部。
        /// </summary>
        /// <param name="response">当前请求的响应对象。</param>
        /// <exception cref="ArgumentNullException"><paramref name="response"/> 为 null。</exception>
        public static void Configure(HttpResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            // text/event-stream 是 SSE 的协议标识，缺失则前端 EventSource 无法建立连接
            response.Headers["Content-Type"] = "text/event-stream";

            // 必须禁用缓存，否则浏览器/代理可能复用上一次的连接响应体
            response.Headers["Cache-Control"] = "no-cache";

            // nginx 默认会缓冲上游响应，导致事件被攒在缓冲区里不实时下发；
            // 这一行是「线上不刷新」问题的直接解法，不要删。
            response.Headers["X-Accel-Buffering"] = "no";
        }
    }
}
