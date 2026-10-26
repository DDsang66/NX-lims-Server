# Domain/Services/DataSheetContext — 数据单模型领域规则

> 本文件是该目录的**协作入口文档**。读完本文档即可掌握本目录全部内容。
>
> 最后更新：基于 `bc627f0`，`DataSheetModelGenerator` 领域化重构 步骤 1 建立骨架。

---

## 1. 目录内容总览

| 文件 | 类型 | 职责 |
|---|---|---|
| `DataSheetModelDomainService.cs` | 领域服务 | 数据单模型生成的跨聚合纯计算规则 |

命名空间：`NX_lims_Softlines_Command_System.src.Domain.Services.DataSheetContext`
（与同级 `ConditionPoolContext` 的组织方式一致。）

---

## 2. 类职责

### `DataSheetModelDomainService`

```csharp
public class DataSheetModelDomainService : IDataSheetModelDomainService, IScopedDependency
```

**为什么放在领域层（代码确证 + 推断）**

它承载的规则横跨三个聚合，任何单一实体都装不下：

| 规则 | 依据 | 所属聚合 |
|---|---|---|
| 分组键剔除 `WashCycle` | 「水洗次数不同不影响参数合并」是业务口径 | `CheckListItem` ↔ `ConditionPool` |
| 数据区个数 = Σ 测点水洗段数 | 同上 | `CheckListItem` ↔ `Template` |
| `Sample_data_{n}` / `After_wash_data_{n}` 书签命名 | 数据单模型与模板之间的契约 | `DataSheetModel` ↔ `Template` |

**设计约束**

- **零仓储、零基础设施依赖**，全部方法为纯内存计算，可脱离数据库单测；
- 刻意不注入 `IStandardRepository` / `ITemplateSelectService` —— 查标准、选模板属应用层编排；
- 标注 `IScopedDependency`（`Domain.Share.DependencyInject`），自动注册。

---

## 3. 方法契约

> 实现状态：**已迁移完成**（步骤 2）。原应用层同名方法尚未移除，待步骤 3 收口。

| 方法 | 入参 | 返回 | 异常 |
|---|---|---|---|
| `PackByParamSet` | `CheckListItem item` | `IReadOnlyList<TestPointPackage>`；无参数配置时返回空列表（非 null） | `ArgumentNullException`（item 为 null） |
| `CalculateDataAreaCount` | `CheckListItem item`、`TestPointPackage package` | `int`；`≤ 0` 表示模板该数据区不适用 | `ArgumentNullException` |
| `ExpandByWashCycle` | `CheckListItem item`、`TestPointPackage package`、`WashDataKind kind` | `string[]`；长度与 `CalculateDataAreaCount` 恒等 | `ArgumentNullException` |
| `BuildBookmarkMap` | `CheckListItem item`、`TestPointPackage package`、`Template template`、`string[] expanded`、`WashDataKind kind` | `BookmarkMapResult`；模板不具备该数据区时返回 `BookmarkMapResult.Empty` | `ArgumentNullException`（template / expanded 为 null） |

**关键不变量（后续实现必须遵守）**

1. `ExpandByWashCycle` 的返回长度 ≡ `CalculateDataAreaCount` 的返回值，两套同构算法不得分叉；
2. `IsAppend` 判定口径为 `capacity > 0 && expanded.Length > capacity`（**严格大于**，相等时刚好填满不复制；`capacity > 0` 是必需防护）；
3. 样本路径与洗后路径的短路判定顺序必须各自保留：
   - `Sample`：不按容量短路，容量为 0 时仍生成映射，仅 `IsAppend` 判为 false；
   - `AfterWash`：先判 `AfterWashDataCount <= 0` 直接返回 `Empty`，连映射都不生成。

### 3.1 步骤 2 核对出的两处「计划与代码不符」（已按真实代码实现）

| # | 计划中的表述 | 代码实际（确证） | 处理 |
|---|---|---|---|
| 1 | 「两份 Expand 方法算法逐行一致，`kind` 只影响注释」 | **算法不同**：`ExpandSampleArray` 写入的是**测点 ID**（同一测点按水洗次数重复），`ExpandAfterWashArray` 写入的是**水洗次数值**（如 1、2、32、45） | `kind` 影响写入值，已在方法内分派，并在 XML 注释里写明 |
| 2 | `IsAppend = expanded.Length >= capacity` | 实际为 `capacity > 0 && expanded.Length > capacity`（严格大于 + 防护） | 按实际实现，`BookmarkMapResult` 的注释同步修正 |

> 另：`ExtractString` / `ExtractInt` **未下沉**。它们服务于应用层 `DataSheetModel` 的 `ReportNumber` / `SheetCount` 组装，不是跨聚合规则；迁入领域服务会成为无调用死代码，故保留在应用层。

---

## 4. 契约类型依赖

| 类型 | 位置 |
|---|---|
| `TestPointPackage` | `src\Domain\Contract\Util\TestPointPackage.cs:5` |
| `WashDataKind` | `src\Domain\Contract\Util\WashDataKind.cs`（本次新建） |
| `BookmarkMapResult` | `src\Domain\Contract\Util\BookmarkMapResult.cs`（本次新建） |
| `CheckListItem` | `src\Domain\Aggregeates\CheckListContext\CheckListItem.cs:12` |
| `Template` | `src\Domain\Aggregeates\TemplateContext\Template.cs:10` |

---

## 5. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建目录与本 README；建立 `DataSheetModelDomainService` 骨架（4 个方法存根，未迁移逻辑） |
| `bc627f0` | 步骤 2：迁入 4 个方法的完整实现（`PackByParamSet` / `CalculateDataAreaCount` / `ExpandByWashCycle` / `BuildBookmarkMap`）；同构展开方法合并为 `ExpandByWashCycle`（§6.8）；书签映射改为同步方法（§6.3 CS1998 消除）；修正原「默认 0 次」注释为「默认 1 次」（§6.4）；`IsAppend` 口径设为严格大于 + 容量防护 |
| `bc627f0` | 步骤 3–5 收口：应用层 `DataSheetModelGenerator` 改为薄壳委托本服务（接口 `IDataSheetModelGenerator` 零变更，Worker 三处调用点零改动）；静态核对确认本服务无 Repository / Infrastructure / ILogger 依赖；`DataSheetService` 同步删除未使用的 `IDataSheetModelGenerator` 注入。 |
| `bc627f0` | 文件由 `README.md` 更名为 `AGENTS.md`：避免与项目根 README 混淆，且复数形式可被主流 AI 编码工具自动发现并挂载为目录上下文。内容未变更。 |

---

## 6. 未来优化方向（后续封装路线）

1. **书签前缀 / 容量策略可配置化**：目前前缀硬编码为 `Sample_data_` / `After_wash_data_`，若新增模板族需支持扩展点。
2. **`ExpandByWashCycle` 支持更多洗涤数据种类**：`WashDataKind` 已是枚举，新增种类只需补分支。
3. **`IsAppend` 与容量判定抽为独立策略**：便于单测边界（等于、少于、多于三种情形）。
4. **`PackByParamSet` 的分组键序列化库统一**：当前沿用 Newtonsoft，与应用层 `System.Text.Json` 混用；统一前需先补分组键稳定性测试。

---

## 7. 待确认项（需业务/架构决策，代码侧未擅自改动）

| # | 待确认项 | 当前实现 | 推荐选项 |
|---|---|---|---|
| 1 | 领域服务是否允许查仓储 | **不允许**（纯函数式，`TestMethod` 由外层查好传入） | 保持不允许；确需数据时改为传入已加载对象 |
| 2 | 是否需要对外暴露 `ExpandSampleArray` / `ExpandAfterWashArray` 原签名 | 只暴露统一入口 `ExpandByWashCycle` | 保持最小 API 面；若出现外部调用方再加一行委托 |
| 3 | `SampleMap` / `AfterWashMap` 适配位置 | 放在应用层 `ToSampleMap` / `ToAfterWashMap` | 保持（不反向依赖应用层）；除非后续要改实体契约 |
| 4 | `IsAppend` 与书签 map 是否拆成两个方法 | 聚合为一个 `BookmarkMapResult` | 保持（原子性强，避免两次遍历不一致） |

> 说明：以上均为**推断性建议**，未经业务确认，未落地为代码。
> 其余遗留缺陷（`ConditionPool` 子集匹配、进度服务死参数、枚举 `Created` 口径等）已在应用层 README §6 登记，本目录不重复。
