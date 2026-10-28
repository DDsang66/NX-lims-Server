# Application/Contract/Realtime — 实时推送契约

> 本文件是该目录的**协作入口文档**。这里放「推送机制需要业务提供什么」的契约，是接入新链路的唯一扩展点。
>
> 最后更新：基于 `bc627f0`，SSE 通用化改造的 M0 步骤引入。

---

## 1. 一句话定位

**推送机制与业务链路之间的接口约定。** 放在 Application 层（而非 `Domain\Contract`）的理由：快照是应用层查询结果的信封，Domain 层不认识「进度」这一应用语义。

## 2. 目录内容总览

| 文件 | 类型 | 职责 |
|---|---|---|
| `ProgressSnapshot.cs` | 值对象（`sealed class`，`init` 只读） | 进度快照信封：业务主键 + 状态 + 指纹 + 下发负载 |
| `IProgressSnapshotProvider.cs` | 接口 | 各业务链路的快照提供者契约（5 个成员） |

## 3. `ProgressSnapshot` 字段契约

| 字段 | 类型 | 含义 | 谁使用 |
|---|---|---|---|
| `Key` | `string` | 业务主键（如 checkListId 字符串） | 日志、排查 |
| `Status` | `string` | 链路总体状态字符串 | **仅 Provider 自己**用于 `IsCompleted` 判定；Hub 不解释 |
| `Fingerprint` | `IReadOnlyDictionary<string,string>` | 参与哈希的业务字段 → 值 | 仅 Provider 用于自描述哈希口径，**不下发前端** |
| `Payload` | `object` | 原样下发给前端的负载（直接放既有 DTO 实例） | 推送枢纽 → `ISseClient.SendAsync` |
| `UpdatedAt` | `DateTimeOffset` | 快照生成时间 | 日志、排查 |

**关键约束：`Payload` 必须放既有 DTO 实例本身**，不要二次包装，否则前端 JSON 结构会变（违反「前端零改动」）。

## 4. `IProgressSnapshotProvider` 方法契约

| 成员 | 入参 | 返回 | 异常 / 备注 |
|---|---|---|---|
| `Channel` | — | `string` | 频道名，必须全局唯一且稳定（如 `"datasheet"`） |
| `GetActiveKeysAsync` | `CancellationToken ct` | `Task<IReadOnlyCollection<string>>` | **不得返回全量业务 key**；无订阅返回空集合（零开销前提） |
| `GetSnapshotAsync` | `string key`、`CancellationToken ct` | `Task<ProgressSnapshot>` | key 已不存在时的行为（抛异常 / 返回终态快照）须在实现方 AGENTS.md 明确 |
| `IsCompleted` | `ProgressSnapshot snapshot` | `bool` | 必须与前端终态口径一致，否则后端关流后前端仍在等 |
| `ComputeHash` | `ProgressSnapshot snapshot` | `string` | 字段顺序、分隔符、时间精度必须**逐字稳定** |

**为什么哈希由 Provider 算（而非 Hub 或委托）**：Hub 必须不认识业务字段，否则通用化失效；委托会让 Hub 持有业务闭包；策略接口只增加间接而无收益。详见 `Application\Service\Realtime\AGENTS.md` §7.3。

## 5. 生命周期约定

实现类应为 **Singleton**（无状态）。若需要 Scoped 依赖（如某个 Scoped 的业务查询服务），
由实现内部通过 `IServiceScopeFactory` 自建 scope 获取，**不要**把 Provider 本身注册为 Scoped ——
它会被 `ProgressPollingService`（HostedService）注入，Scoped 会造成 captive dependency。

## 6. 已有实现（跨目录索引）

| Provider | 位置 | Channel |
|---|---|---|
| `DataSheetProgressSnapshotProvider` | `src\Application\Service\DataSheetContext\` | `datasheet` |
| （第二条链路，待接入） | `src\Application\Service\<新业务>Context\` | 待定 |

## 7. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建目录。新增 `ProgressSnapshot` 与 `IProgressSnapshotProvider`，作为「推送机制 ↔ 业务链路」的唯一契约边界。设计上刻意让 `Fingerprint` 存在但不参与下发，目的让哈希口径在 Provider 内自描述、便于逐字复刻既有实现。 |
| `bc627f0` | **M1 落地验证**：首个实现 `DataSheetProgressSnapshotProvider`（频道 `datasheet`）已按本契约完成，证明「5 个成员 + Singleton 生命周期 + 内部 `CreateScope`」这套约定可落地；其 `ComputeHash` 逐字复刻了原 `DataSheetProgressHub` 的硬编码口径，`IsCompleted` 复刻原轮询服务的终态判定。 |

## 8. 未来优化方向

1. **哈希样板收敛**：各 Provider 的 `SHA256 → Convert.ToHexString` 样板重复若超过 2 处，抽成静态辅助类或 Provider 基类（当前只有 1 处，按「优化后置」原则不动）。
2. **Fingerprint 的实际使用**：当前仅作自描述，未参与计算。若将来统一由基类按 `Fingerprint` 计算哈希，可消除各 Provider 的手写拼接，但会削弱「逐字复刻既有口径」的可控性，需评估。
3. **契约版本化**：若 `ProgressSnapshot` 字段变化，需考虑存量 Provider 的兼容（可加 `SchemaVersion`）。
