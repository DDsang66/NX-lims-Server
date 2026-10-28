# Domain/Services — 领域服务层

> 本文件是该目录的**协作入口文档**。接手前请先读完本文档，即可掌握：
> 本目录承载什么、按什么规则分目录、现有成员清单、领域服务的依赖边界。
>
> 最后更新：基于 `bc627f0`，随 `DataSheetModelGenerator` 领域化重构引入。

---

## 1. 目录定位

本目录存放**领域服务**：不属于任何单一聚合、又必须由领域专家口径决定的规则集合。

依据（代码确证）：
- `ParamEngineScheduler` 负责取参数规则数据并调度生成（`ParamEngineScheduler.cs:24`）；
- `ConditionPatternBuilder`、`Parser`、`Tokenizer`、`ValueComparer` 是跨聚合通用工具；
- `TemplateIdGenerator`、`TemplateQueryService` 服务于模板聚合。

**划分原则（推断，基于目录实测归纳）**：
- 某个聚合专属的服务 → 放进以聚合命名的子目录 `XxxContext/`；
- 跨聚合的通用能力 → 放根目录，或按功能建目录（`Compensation/`、`Validate/`）。

---

## 2. 目录结构总览

| 位置 | 内容 |
|---|---|
| 根目录 | `ConditionPatternBuilder.cs`、`ParamEngineScheduler.cs`、`ParamGenerationEngine.cs`、`Parser.cs`、`TemplateIdGenerator.cs`、`TemplateQueryService.cs`、`Tokenizer.cs`、`ValueComparer.cs` |
| `ConditionPoolContext/` | `ConditionAccessor.cs`、`ConditionEnricher.cs`、`ConditionPoolComparisonService.cs`、`ConditionPoolDomainService.cs`、`GenerateRequiredConditionsService.cs`、`GroupPoolsAsync.cs` |
| `FiberCompositionContext/` | `FiberCompositionHelper.cs` |
| `Compensation/` | `ParamCompensationService.cs` |
| `Validate/` | `ConditionPoolValidateService.cs`、`FileSecurityValidator.cs`、`FormulaValidateService.cs`、`ParamRuleValidateService.cs`、`ParamStructureValidateService.cs`、`ParamValidateService.cs` |
| `DataSheetContext/` | `DataSheetModelDomainService.cs`（新增，见该目录 README） |
| `StandardContext/` | `StandardMethodCodeResolver.cs`（新增，见该目录 AGENTS.md）：标准号 → 方法级键的归一化，零依赖纯函数 |

---

## 3. 领域服务的依赖边界

本项目的实际容忍度存在两种并存风格，**新建服务应按「纯函数式」写**：

| 风格 | 代表 | 依赖 |
|---|---|---|
| 纯函数式（推荐基准） | `ConditionPoolContext\ConditionPoolDomainService.cs:9` | 零仓储、零 Infrastructure、零 ILogger，全部内存计算 |
| 允许查库（历史现状） | `ParamEngineScheduler.cs:26-30` | 注入 4 个仓储 + `ILogger`，含 `async` 查库方法 |

**新增 `DataSheetModelDomainService` 的取舍（代码确证）**：零依赖。查标准、选模板属于应用层编排，所需数据由外部传入。

---

## 4. 命名与注册约定（代码确证）

- 一律标注 `IScopedDependency`（位于 `Domain.Share.DependencyInject`），由容器自动扫描注册，**无需手工注册代码**。
- 命名后缀按语义选择，无强制约束：生成结构用 `Engine` / `Builder` / `Generator`，聚合级规则集合用 `DomainService`，单一能力用 `Accessor` / `Enricher` / `Helper` / `Validator`。
- 接口统一放在 `src\Domain\Contract\Service\` 下，按主题进子目录；详见该目录 README。

---

## 5. 已知问题（仅登记，勿顺手修）

| # | 问题 | 位置 |
|---|---|---|
| 1 | `Enrich` 方法直接 `return null;`，未实现但已注册进 DI | `ConditionPoolContext\ConditionPoolDomainService.cs:53` |
| 2 | 残留 `Console.WriteLine` 调试输出 | `ConditionPoolContext\ConditionPoolDomainService.cs:85` |
| 3 | `GroupPoolsAsync` 类名带 `Async`，但其 `Group` 方法是同步的 | `ConditionPoolContext\GroupPoolsAsync.cs:9`、`:19` |
| 4 | 文件名 / 类名 / 接口名三重不一致：`TemplateQueryService.cs` ↔ `TemplateSelectService` ↔ `ITemplateSelectService` | `TemplateQueryService.cs:12` |

---

## 6. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建本 README；新增 `DataSheetContext\DataSheetModelDomainService.cs`（数据单模型领域规则下沉，零仓储依赖） |
| `bc627f0` | 数据单领域化重构完成：4 个方法（分包 / 数据区计数 / 水洗展开 / 书签映射）全部实现在领域层，应用层改为薄壳委托。静态核对确认本服务**无 Repository / Infrastructure / ILogger 依赖**（代码确证：`grep Repository\|Infrastructure\|ILogger` 仅命中 XML 注释）。 |
| `bc627f0` | 文件由 `README.md` 更名为 `AGENTS.md`：避免与项目根 README 混淆，且复数形式可被主流 AI 编码工具自动发现并挂载为目录上下文。内容未变更。 |
| `ffa16de` | 新增 `StandardContext\StandardMethodCodeResolver.cs`：标准号 → 方法级键（如 `BS EN ISO 12945-2:2010` → `ISO 12945-2`）归一化，供模板索引按「测试方法」而非「具体标准号」匹配。**零依赖纯函数**（不注入仓储）；解析失败返回 null 不抛异常。选择理由与「为何不用标准族」见该目录 AGENTS.md。 |

---

## 7. 未来优化方向

1. 统一 `Console.WriteLine` 排查口径，全部替换为 `ILogger` 或删除（对应上表 #2）。
2. 收敛「允许查库」与「纯函数式」两种风格：建议领域服务一律纯函数式，取数上移到应用层。
3. 修正 `TemplateQueryService.cs` 的三重命名不一致（改名需同步文件名，建议单独一次提交）。
