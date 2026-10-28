# Application/Service/Realtime — 通用实时推送编排

> 本文件是该目录的**协作入口文档**。接手前请先读完，即可掌握：推送枢纽与轮询服务的契约、如何接入新链路、多副本部署前必须做什么。
>
> 最后更新：基于 `bc627f0`，SSE 通用化改造的 M0 步骤引入。

---

## 1. 一句话定位

**把「谁要推」与「怎么推」编排起来：`SseHub` 管连接与广播，`ProgressPollingService` 管按秒取快照并派发。**

它不认识任何业务字段——业务差异全部由 `IProgressSnapshotProvider` 的实现（在各业务目录下）承担。

## 2. 目录内容总览

| 文件 | 类型 | 生命周期 | 职责 |
|---|---|---|---|
| `ISseHub.cs` | 接口 | — | 链路无关的推送枢纽契约 |
| `SseHub.cs` | 实现 | **Singleton**（`ISingletonDependency`） | 连接表 + 哈希去重 + dead 清理 + 终态关流 |
| `ProgressPollingService.cs` | `BackgroundService` | HostedService | 每秒一轮，遍历所有 Provider 派发 |

## 3. 方法契约

### `ISseHub`

| 方法 | 入参 | 返回 | 异常 / 备注 |
|---|---|---|---|
| `RegisterAsync` | `string channel`、`string key`、`ISseClient client`、`CancellationToken ct` | `Task` | `ArgumentException`（channel/key 空白）；`ArgumentNullException`（client 为 null） |
| `UnregisterAsync` | `string channel`、`string key`、`ISseClient client` | `Task` | key 不存在时为无操作 |
| `GetActiveKeys` | `string channel` | `IReadOnlyCollection<string>` | 只返回**连接非空**的 key；channel 空白返回空集合 |
| `HasChanged` | `string channel`、`string key`、`string hash` | `bool` | 与上次相同返回 false；不同则更新并返回 true |
| `BroadcastAsync` | `channel`、`key`、`eventName`、`object payload`、`ct` | `Task` | 单连接失败不中断其它连接，失败的会进入 dead 清理 |
| `CloseAllAsync` | `channel`、`key`、`ct` | `Task` | 移除连接表项 + 逐个 `CompleteAsync` + 清哈希；关闭失败只记 Debug |

### `ProgressPollingService`

| 成员 | 说明 |
|---|---|
| 构造函数 | `(ISseHub hub, IEnumerable<IProgressSnapshotProvider> providers, ILogger logger)`；任一为 null 抛 `ArgumentNullException` |
| `ExecuteAsync` | 1 秒一轮；启动时打日志列出已注册频道（新增链路是否被纳入，看这行即可） |
| 事件名 | `progress`（常规）、`completed`（终态）—— **对外契约，不可随意改** |

## 4. ★ 接入新链路的唯一扩展点

新增一条业务链路只需三步，**不需要修改本目录任何文件、也不需要改 DI 注册代码**：

1. 在业务目录（如 `Application\Service\XxxContext\`）新建 `XxxProgressSnapshotProvider : IProgressSnapshotProvider, ISingletonDependency`；
2. 在 `Application\Contract\Realtime\` 的契约下实现 5 个成员（Channel / GetActiveKeys / GetSnapshot / IsCompleted / ComputeHash）；
3. 在 Web API 加一个 `StreamProgress` 端点（骨架与 `DataSheetController` 同构）。

`ProgressPollingService` 因注入 `IEnumerable<IProgressSnapshotProvider>` 会**自动发现**并纳入派发。

### 4.1 第二条链路：待接入（本次已确认跳过）

本次改造的交付范围是「**设施抽象 + DataSheet 作为第一个消费者**」。第二条链路因业务尚未确定，
**未落地任何代码**——按约定不留占位类、不留死代码。真正接入前必须先确认以下 4 项：

| # | 待确认 | 影响 |
|---|---|---|
| 1 | 业务对象与**主键类型**（现有 DataSheet 用 `Guid`） | 决定 `key` 的生产方式与 `Channel` 命名 |
| 2 | 该链路的**状态枚举**（进行中 / 成功 / 失败 / 部分成功的具体取值） | 决定 `IsCompleted` 的判定集合；必须与前端终态口径一致，否则后端关流后前端仍在等 |
| 3 | 前端已约定的**事件名**（沿用 `progress` / `completed` / `heartbeat` 还是自定义） | 决定端点实现；事件名属对外契约，**不能事后偷改** |
| 4 | 快照是否需要**分布式缓存**（DataSheet 侧 TTL 仅 0.5 秒） | 数据量小可直查 DB；高频轮询则需缓存，否则每秒一轮会给 DB 压力 |

接入时的落地清单（照此执行即可，**不需要改动本目录任何文件**）：

1. `Application\Service\<新业务>Context\<新业务>ProgressSnapshotProvider.cs`
   —— Singleton + `ISingletonDependency`，内部用 `IServiceScopeFactory` 取 Scoped 业务服务；
2. `<新业务>Controller` 增加 `StreamProgress` 端点
   —— 骨架复制 `DataSheetController`，只改 3 处：频道名、Provider、路由；
3. 新建该业务目录的 `AGENTS.md`，并在本文件 §2 的频道登记中补一行。

> **自检方法**：启动服务后看 `ProgressPollingService` 的启动日志，
> 它会把所有已注册频道打印出来（`ProgressPollingService 已启动，注册的进度链路：...`）。
> 新增链路的名字出现在这一行，即表示已被自动纳入，无需任何额外注册。

## 5. 关键设计约束（改造前必读）

| # | 约束 | 原因 |
|---|---|---|
| 1 | **Hub 不得认识任何业务字段** | 否则第二条链路进来就得改 Hub，通用化失效。哈希由 Provider 计算 |
| 2 | **channel 必须全局唯一且稳定** | Hub 用 `"channel:key"` 作复合键，频道冲突会导致跨业务串推 |
| 3 | **`GetActiveKeys` 不得返回全量业务 key** | 这是「无人订阅时不查库」的前提，违反即失去零开销特性 |
| 4 | **哈希口径必须逐字稳定** | 字段顺序、分隔符、时间精度任一变化都会导致无意义重复推送 |
| 5 | **`IsCompleted` 必须与前端终态口径一致** | 否则后端关流后前端仍在等 `completed` |
| 6 | **Provider 应为 Singleton** | 若为 Scoped 被 BackgroundService 注入会形成 captive dependency；需要 Scoped 依赖时由 Provider 内部用 `IServiceScopeFactory` 自建 scope |

### 与原 `DataSheetProgressHub` 的一处行为修正

原实现的 `Unregister` 在移除最后一个连接后**保留空 Bag**，导致 `GetActiveCheckListIds` 仍返回该 key，轮询服务继续为「已无人订阅」的 key 查库，与「无订阅零 DB 开销」的设计目标不符。

`SseHub.UnregisterAsync` 在连接集合为空时**一并移除该 key**，`GetActiveKeys` 也过滤空 Bag。这会让 DataSheet 链路少查一些无用快照（只会减少查询，不会漏推）。

## 6. ⚠️ 多副本部署前必做改造清单（本次未做）

`SseHub` 的连接表是**进程内** `ConcurrentDictionary`。多副本部署时：

```
副本 A 的浏览器订阅 → 只存在于副本 A 的内存
副本 B 完成生成并广播 → 只推给副本 B 的内存连接（空）→ 前端一直显示进行中 ❌
```

上线多副本前必须完成：

| # | 改造 | 理由 |
|---|---|---|
| 1 | 连接表外置（Redis Hash/Set，`channel:key → clientId 集合`），处理 TTL 与心跳续期 | 让各副本看到同一份连接清单 |
| 2 | 广播改走 Redis pub/sub 或 SignalR backplane：任一副本产生变更 → `publish(channel, key, hash, payload)` → 各副本只推本地持有的连接 | 跨副本事件传递 |
| 3 | 哈希去重改分布式（`_lastSnapshotHash` 放 Redis 带 TTL） | 否则每副本各推一次，前端收到 N 份重复事件 |
| 4 | 轮询派发改「单副本执行」（分布式锁 / 选主） | 否则 N 副本 × 每秒一轮 = N 倍 DB 压力 |
| 5 | SSE 长连接与副本的粘性：会话粘滞，或改为 backplane 广播（**推荐后者**） | 长连接不粘滞时订阅与推送落在不同副本 |

**当前判断（与用户确认）**：本次**不做**，仅登记。理由：当前部署形态未确证为多副本，且本次改动面已较大，叠加 Redis 会引入新故障域。

## 7. 关键取舍记录

### 7.1 保留轮询 vs 事件驱动（`Channel<T>` / 领域事件）

**采用轮询。** 理由：
- 现有「哈希去重 + 只轮询活跃订阅者」已把 DB 开销压到最低；
- 事件驱动需要业务侧在每次状态变更时主动 publish，会侵入 `DataSheetGenerateWorker` 与所有写入路径，改动面远大于收益；
- 失败重试/回补场景**仍需轮询兜底**，事件驱动无法替代，只会叠加复杂度。

事件驱动列为未来优化方向，等「推送实时性要求 < 1 秒」成为硬需求时再评估。

### 7.2 自研 SSE vs SignalR

**采用自研 SSE。** 理由：
- 前端已用 `EventSource` 对接，事件名固定；换 SignalR 需引入 `@microsoft/signalr` SDK 并改连接方式，**违反「前端零改动」硬要求**；
- SignalR 的核心价值（跨实例 backplane、自动重连、组播）只在多副本场景才体现，而该场景已单独登记。

### 7.3 哈希计算职责归属

**由 Provider 实现。** 理由：
- Hub 若硬编码业务字段则失去通用性；
- 委托 lambda 会让 Hub 持有业务闭包，形成隐式耦合；
- 独立 `ISnapshotHasher` 策略接口只增加一层间接，收益不明显；
- Provider 本就掌握该链路全部业务字段，是自然归属。样板（`SHA256 → hex`）若重复超过 2 处，再考虑抽基类。

## 8. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建目录。`ISseHub` / `SseHub` / `ProgressPollingService` 从 `DataSheetProgressHub` / `DataSheetProgressPollingService` 泛化而来：① 键从单一 `Guid` 改为 `(channel, key)`；② 删除硬编码业务字段的 `ComputeHash`，改由 Provider 提供；③ 轮询服务从「硬编码 DataSheet 快照」改为「遍历 Provider 集合」，新链路接入零改动；④ `UnregisterAsync` 在连接集空时移除 key（修正原实现空 Bag 仍被轮询的问题）。 |
| `bc627f0` | **M1 落地**：`DataSheetProgressHub` 改为兼容薄壳（转发本目录 `ISseHub`）、新增 `DataSheetProgressSnapshotProvider`、`DataSheetProgressPollingService` 停用，`Program.cs` 改注册本目录的 `ProgressPollingService`（避免双轮询）。**M3 收口**：第二条链路因业务未定**未落地任何代码**（不留占位），接入前必确认的 4 项与落地清单见 §4.1。 |

## 9. 待确认项（不代决策，实施时逐项确认）

| # | 待确认 | 当前默认 / 推荐 |
|---|---|---|
| 1 | `CompleteAsync` 在 Kestrel 下是否真的让客户端立即感知断开 | 待实测；不足则改 `HttpContext.Abort()` |
| 2 | 是否需要 `Last-Event-ID` 断线重放 | 暂不需要（前端靠重连拿全量快照） |
| 3 | 是否引入事件驱动替代轮询 | 暂不引入（见 §7.1） |
| 4 | 新链路的主键类型与事件名 | 待业务确认 |
| 5 | 多副本 backplane 的上线门槛 | 见 §6 清单，本次不做 |
| 6 | Provider 的 `GetSnapshotAsync` 在 key 已不存在时的约定（抛异常 vs 返回终态快照） | 由各 Provider 在自身 AGENTS.md 明确 |
