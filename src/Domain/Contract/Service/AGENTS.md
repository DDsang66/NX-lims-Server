# Domain/Contract/Service — 领域服务契约

> 本文件是该目录的**协作入口文档**。读完本文档即可掌握接口放置规格与现有接口清单。
>
> 最后更新：基于 `bc627f0`，随 `DataSheetModelGenerator` 领域化重构引入。

---

## 1. 目录内容总览

| 位置 | 接口 |
|---|---|
| 根目录 | `IFileSecurityValidator.cs`、`ITemplateIdGenerator.cs`、`ITemplateQueryService.cs`、`ITemplateValidator.cs`、`IWorkdayCalculator.cs` |
| `Engine/` | 引擎族接口（含 `Engine/Condition/IConditionPoolDomainService` 等） |
| `DataSheetContext/` | `IDataSheetModelDomainService.cs`（本次新增） |

---

## 2. 接口放置规格（代码确证）

1. **一文件一接口**为主，但同一主题可聚合：`ITemplateQueryService.cs:9` 内同时定义了 `ITemplateSelectService`。
2. **按主题进子目录**：引擎族在 `Engine/` 下再按类别分子目录（如 `Engine/Condition/`）；新增的数据单模型契约放 `DataSheetContext/`，不塞进根目录。
3. **实现方法**：实现类统一标注 `IScopedDependency`（位于 `Domain.Share.DependencyInject`），由容器自动扫描注册，无需手工注册。

---

## 3. 本次新增接口

### `DataSheetContext\IDataSheetModelDomainService.cs`

命名空间：`NX_lims_Softlines_Command_System.src.Domain.Contract.Service.DataSheetContext`

| 方法 | 用途 |
|---|---|
| `PackByParamSet(CheckListItem)` | 参数分包（分组键剔除 `WashCycle`） |
| `CalculateDataAreaCount(CheckListItem, TestPointPackage)` | 数据区格数 |
| `ExpandByWashCycle(CheckListItem, TestPointPackage, WashDataKind)` | 按洗涤次数展开 |
| `BuildBookmarkMap(CheckListItem, TestPointPackage, Template, string[], WashDataKind)` | 书签映射 + `IsAppend` 判定 |

实现：`src\Domain\Services\DataSheetContext\DataSheetModelDomainService.cs`（零仓储依赖）。

---

## 4. 已知问题（仅登记）

| # | 问题 | 位置 |
|---|---|---|
| 1 | 文件名与接口名不一致：`ITemplateSelectService` 定义在 `ITemplateQueryService.cs` 内 | `ITemplateQueryService.cs:9` |
| 2 | 部分接口未继承 DI 标记（如 `IWorkdayCalculator`、`IParamLimitation` 类），需手工注册 | 推断，实施新接口前需逐个确认 |

> 建议：新增接口一律继承 `IScopedDependency`，落入自动扫描。

---

## 5. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建本 README；新增 `DataSheetContext\IDataSheetModelDomainService.cs`（数据单模型领域规则契约，4 个方法） |
| `bc627f0` | 契约经实测稳定：实现类 `DataSheetModelDomainService` 按该契约落地，应用层 `DataSheetModelGenerator` 依赖此接口完成委托。`IDataSheetModelGenerator`（Application 层）**未改动**，仍只暴露 `GenerateAsync`。 |
| `bc627f0` | 文件由 `README.md` 更名为 `AGENTS.md`：避免与项目根 README 混淆，且复数形式可被主流 AI 编码工具自动发现并挂载为目录上下文。内容未变更。 |

---

## 6. 未来优化方向

1. `ITemplateQueryService.cs` 改名为 `ITemplateSelectService.cs`（与接口名对齐），需同步 `ITemplateQueryService` 引用方。
2. 为未继承 DI 标记的接口补 `IScopedDependency`，消除手工注册。
3. 补一份「接口 → 实现类 → 生命周期」索引表，便于排查 DI 解析失败。
