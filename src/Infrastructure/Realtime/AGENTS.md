# Infrastructure/Realtime — 实时传输技术实现

> 本文件是该目录的**协作入口文档**。接手前请先读完，即可掌握：本目录解决什么问题、每个类的方法契约、为什么放在 Infrastructure 而不是 Domain/Web。
>
> 最后更新：基于 `bc627f0`，SSE 通用化改造的 M0 步骤引入。

---

## 1. 一句话定位

**SSE 的「怎么把字节写到 HTTP 响应」这一层技术实现。** 只关心协议格式与响应流，不认识任何业务字段。

## 2. 为什么放在 Infrastructure（不是 Domain，也不是 Web API）

| 候选层 | 结论 | 理由 |
|---|---|---|
| `Domain\Contract\Util`（原位置） | ❌ 必须搬走 | 原 `SseEmitter` 持有 `HttpResponse`（ASP.NET Core 类型），使**领域层被 Web 框架绑架**，与项目「Domain 不依赖上层/框架」的约定冲突 |
| `Web API` | ❌ 不可 | 会让 Application 层的推送枢纽反向依赖 Web 层（Hub 需要持有 `ISseClient`） |
| `Infrastructure\Realtime\Sse` | ✅ 采用 | 与既有 `TemplateEngine` / `WordTemplateAdapter` 同构：Infrastructure 承载技术实现，上层只依赖其抽象 |

## 3. 目录内容总览

| 文件 | 类型 | 职责 |
|---|---|---|
| `Sse\ISseClient.cs` | 接口（出站端口） | SSE 连接抽象，隔离 `HttpResponse` |
| `Sse\HttpResponseSseClient.cs` | 实现（`sealed`） | 把事件写到当前请求的 `HttpResponse` |
| `Sse\SseResponseConfigurator.cs` | 静态工具 | 统一配置 3 个 SSE 响应头 |

## 4. 方法契约

### `ISseClient`

| 成员 | 入参 | 返回 | 异常 / 备注 |
|---|---|---|---|
| `Id` | — | `string` | 连接唯一标识（GUID），用于日志与 dead 追踪 |
| `IsClosed` | — | `bool` | 关闭后 `SendAsync` 直接返回而不抛异常 |
| `SendAsync` | `string eventName`、`object payload`、`CancellationToken ct` | `Task` | `ObjectDisposedException`（底层流已释放）→ 调用方应捕获并把该连接视为 dead |
| `CompleteAsync` | `CancellationToken ct` | `Task` | **不抛异常**（内部吞掉刷出失败并降级为 Debug 日志） |

### `HttpResponseSseClient`

| 成员 | 说明 |
|---|---|
| 构造函数 | `(HttpResponse response, ILogger<HttpResponseSseClient> logger)`；任一为 null 抛 `ArgumentNullException` |
| 生命周期 | **每次请求 new**，不注册进 DI（必须绑定具体 `HttpResponse` 实例） |
| `SendAsync` | 检 `IsClosed` → `SemaphoreSlim` 串行化 → 序列化 → 逐行写 `data: ` → 空行 → `FlushAsync` |
| `CompleteAsync` | 置 `IsClosed = true` + 尽力 flush（详见下节限制） |

### `SseResponseConfigurator.Configure(HttpResponse)`

设置 3 个必备头，缺一不可：

| header | 值 | 缺失后果 |
|---|---|---|
| `Content-Type` | `text/event-stream` | 前端 `EventSource` 无法建立连接 |
| `Cache-Control` | `no-cache` | 浏览器/代理可能复用旧响应体 |
| `X-Accel-Buffering` | `no` | **nginx 会缓冲上游响应，事件被攒住不实时下发**（线上「本地正常、线上不刷新」的直接原因） |

## 5. 关键实现细节（改动前必读）

1. **`SemaphoreSlim(1,1)` 串行化写入**：同一响应 Body 被并发写会产生交错字节流，前端解析失败。**不要删**。
2. **`data:` 多行拆分**：SSE 规范要求 `data` 值含换行时拆成多条 `data: `，否则 `EventSource` 会把换行前内容当成完整事件、后续丢弃。原实现直接写单行，对含换行的 JSON 是隐患，本次补齐。
3. **`CompleteAsync` 不能真正断开 TCP**：`HttpResponse` 没有公开的 Complete API，真正的连接终止由请求管道在 action 返回时完成。本方法只做「置状态 + 尽力 flush」。若需强制断开，应改为持有 `HttpContext` 并调 `Abort()`（见 §7 待确认项）。
4. **已关闭连接不再尝试写入**：避免调用方反复捕获异常、dead 清理逻辑空转。

## 6. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建目录。`ISseClient` / `HttpResponseSseClient` / `SseResponseConfigurator` 从「Domain 层 `SseEmitter` + Controller 手写 header」演进而来：① 分层归位（Domain → Infrastructure）；② 补 `data:` 多行拆分；③ `CompleteAsync` 替代原空实现 `Complete()`；④ header 集中配置避免第二条链路手抄。 |
| `bc627f0` | **M2 收口**：`src\Domain\Contract\Util\SseEmitter.cs` 已删除（删除前全库 grep 确认零生产引用）；复核 `src\Domain` 下 `Microsoft.AspNetCore` 命中数为 **0**，分层违规彻底清除。`DataSheetController.StreamProgress` 已改用本目录的 `HttpResponseSseClient` + `SseResponseConfigurator`，URL / 事件名 / payload 结构零变更。 |

## 7. 未来优化方向 / 待确认项

1. **`CompleteAsync` 的真实关流行为需实测**：Kestrel 下 `FlushAsync` 是否足以让客户端立即感知断开，需用真实 SSE 连接验证。若不足，改为注入 `IHttpContextAccessor` 或直接持有 `HttpContext` 调 `Abort()`。
2. **`Last-Event-ID` 重放**：当前接口预留了扩展空间但未实现。若前端需要「断线后补发丢失事件」，需在 `SendAsync` 增加 `id:` 字段并在端点读取请求头。
3. **`retry:` 字段**：当前未写。若前端重连间隔需要服务端控制，应在首帧补 `retry: 3000`。
4. **背压**：慢客户端会让 `SemaphoreSlim` 排队，极端情况下拖慢广播。当前靠「写失败即判 dead」兜底，未做队列长度限制。
5. **压缩**：SSE 长连接默认不压缩；若单事件 payload 很大，可评估 gzip 与 `X-Accel-Buffering` 的兼容性。
