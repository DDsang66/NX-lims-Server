# Application/Service/CheckListContext — 检测清单应用服务

> 本文件是该目录的**协作入口文档**。接手前请先读完本文档，即可掌握：
> 本目录有哪些服务、各自职责、方法契约、生成时序约定与错误码。
>
> 最后更新：基于 `bc627f0`，checklist testParam 改造引入。

---

## 1. 目录定位

本目录承载 **检测清单（CheckList）的应用层编排**：清单参数计算、清单查询与清洗、以及
checklist docx 的生成编排。业务规则不写在这里——纯计算在领域服务，模板文本合成在公共组件。

---

## 2. 目录内容总览

| 文件 | 类型 | 职责 |
|---|---|---|
| `CheckListAppService.cs` | 应用服务（`IScopedDependency`） | 对外入口：参数计算、清单查询、checklist docx 生成编排 |
| `CheckListTestParamResolver.cs` | 应用服务（`IScopedDependency`） | checklist 侧 testParam 补齐：查条件池 → 分包合成 testcondition → 回写 DTO |

对应契约：
- `src\Application\Interface\ICheckListAppService.cs`
- `src\Application\Interface\ICheckListTestParamResolver.cs`（本次新增）

---

## 3. `CheckListAppService` 方法契约

| 方法 | 入参 | 返回 | 说明 |
|---|---|---|---|
| `CalculateParamAsync` | `Guid id`、`CancellationToken ct` | `Task<Result>` | 查条件池 → 按测点找池 → `GenerateForCheckListItemAsync` 得 `ParamSet` → `UpdateItemParameters` 落库 |
| `GetCheckListAsync` | `Guid checkListId`、`CancellationToken ct` | `Task<Result<CheckListResponseDto>>` | 供前端展示；内部用 `ProcessParameterCutting` 剥离 `SpecimenArea`/`SpecimenNum`/`Remark` 并回填 `CuttingMethod` |
| `GenerateCheckListAsync` | `CheckListGenerateDto dto`、`CancellationToken ct` | `Task<Result<DocxUrlResponseDto>>` | 生成 checklist docx；在调 Adapter 前先调 `ICheckListTestParamResolver` 补齐 testParam，失败则结构化日志 + 阻断 |

**依赖注入（8 个）**：`IUnitOfWork`、`ICheckListRepository`、`CheckListAdapter`、
`IFileStorageService`、`IConditionPoolRepository`、`IParamGenerationUseCaseService`、
`ICheckListTestParamResolver`、`ILogger<CheckListAppService>`。

> ⚠️ `ProcessParameterCutting` 会**移除** `SpecimenArea` / `SpecimenNum` / `Remark`，它只服务于
> `GetCheckListAsync` 的展示链路。**checklist docx 生成链路不经过它**，直接从聚合与条件池计算——
> 两条链路不要混用，否则会出现「先剔字段再算条件」导致的文本缺项（静默错误）。
>
> 提取后 `CuttingMethod` 的形态：每个测点组拼为 `SpecimenArea=…, SpecimenNum=…, Remark=…`
> （只拼存在的字段，顺序固定），多测点组以 `"; "` 连接；三个字段任一非空即成组，
> 因此「只有 Remark 没有 Specimen」的测点组也不会被漏掉。

---

## 4. `CheckListTestParamResolver` 契约

### `ApplyAsync(CheckList checkList, CheckListGenerateDto dto, CancellationToken ct)`

**职责链**：查条件池 → 为每个检测项定位命中的池 → 调 `ITestConditionComposer.ComposeForItemAsync`
（分包 + 逐包合成 + 组内去重）→ 覆盖 dto 条目的 `Parameter` → 组内去重后覆盖 `CuttingMethod`。

| 项 | 内容 |
|---|---|
| 入参 `checkList` | 已从库加载的聚合，须含 `Items` 且各 Item 的 `TestPointParams` 已落库 |
| 入参 `dto` | 前端生成请求；**仅就地覆盖** `Parameter` 与 `CuttingMethod`，其余字段一律不动 |
| 返回 | 无（结果通过修改 `dto` 体现） |
| 异常 | `TestConditionCompositionException`（TC001–TC005）；`ArgumentException`（dto 与聚合条目数不一致、或按 `TestItem` 找不到对应条目） |

**两条实现约定**：

1. **对齐失败必须报错**：dto 条目与聚合条目按 `TestItem` 字符串对齐，数量不一致或键值找不到均抛
   `ArgumentException`。绝不按列表下标盲配——错位写入是最难排查的静默故障。
2. **不注入 `ILogger`**：异常一律上抛，由 `CheckListAppService.GenerateCheckListAsync` 统一记录结构化日志，避免同一失败记录两次。

### dto 条目对齐策略（`ResolveDtoItem`）

**这是本服务最容易踩坑的地方，改之前必读。**

前端回传的 `dto.Items[].TestItem` **不是可靠的标识**：后端映射时给的是 `TestItemId.Value`（`CheckListMappingConfig.cs:123`），
但前端往往把它替换成展示名称（参考样本 docx 的 Test Item 列就是 `NameEn(NameChn)` 拼接形态）。

因此对齐采用三级降级，**不使用位置下标兜底**：

| 级别 | 匹配键 | 查库 | 覆盖场景 |
|---|---|---|---|
| 1 | `dto.TestItem` 精确等于 `TestItemId.Value` | 无 | 前端原样透传 `GetCheckListAsync` 结果 |
| 2 | `dto.TestItem` 包含 `TestItem.NameEn` / `NameChn`（或反向包含，应对截断） | 按需 1 次全量查 | 前端展示为 `NameEn(NameChn)` |
| 3 | **Samples 测点集合相等**消歧 | 无（纯内存） | 第 2 级出现多候选时缩小范围 |
| 兜底 | 抛 `TC007` | — | 0 个或仍多于 1 个候选 |

**两条硬约束**：

1. **Samples 不能单独做主键**：同一 checklist 内「多个检测项测点完全相同」的情况很常见，
   它只能缩小候选、不能唯一定位，真正的主键是 `TestItemId`（经由名字或 ID 匹配到）。
2. **不做位置索引兜底**：`dtoItems[i]` 与 `checkList.Items[i]` 按顺序对应看似简单，
   但前端一旦排序或增删行就会**静默写错行**——比直接报错难排查得多。

**性能**：`ITestItemRepository` 未提供按 ID 批量查询的方法，故第 2 级触发时取一次全量
`GetAllTestItemsAsync` 并在内存建索引；若全部条目都能按 ID 精确命中（第 1 级），
**完全不查库**。checklist 生成是低频人工操作，该开销可接受。

### CuttingMethod 去重口径

业务口径（用户确认）：**组内去重，相同值只写首行**。

**两种分隔形态都要支持**：
- `; `（分号+空格）分隔——本项目当前的实际形态，源头是 `CheckListAppService.ProcessParameterCutting`
  里的 `string.Join("; ", parts)`，同一检测的多个测点各贡献一段；
- 换行分隔的多行文本。

实现：先按行拆、再对每行按 `;` 拆片段 → `Distinct(Ordinal)` → 按原形态回拼
（原文含换行则用 `\r\n`，否则用 `; `）。对单值输入天然幂等。

> 历史教训：最初只按换行去重，导致 `SpecimenArea=500mm×500mm; SpecimenArea=500mm×500mm; ...`
> 原样重复输出，去重形同虚设。

---

## 5. 生成时序约定（显式化的隐性约定）

> 以下是**约定而非缺陷**，请勿当作 bug 修复：

1. **checklist docx 生成依赖四要素齐备**：`ConditionPool` 存在且覆盖该测点、
   `CheckListItem.TestPointParams` 已落库、模板索引能命中模板、模板含对应 testcondition 文本模板。
2. **允许改变 checklist 与 datasheet 的生成先后顺序**，业务流程不受影响；
   但 checklist 生成时刻上述四要素必须齐备，否则**明确报错阻断**（见错误码表），不会产出空单元格的 docx。
3. **两条链路共用同一份 testcondition 实现**（`ITestConditionComposer`），
   因此同一 checklist 生成的 datasheet 与 checklist 中，条件文本必须一致。

---

## 6. 错误码表

| Code | 含义 | 排查动作 |
|---|---|---|
| `TC001` | 未找到该 checklist 的条件池 | 先完成参数计算，使 `ConditionPool` 落库 |
| `TC002` | 检测项的测点在条件池中均未命中 | 检查 `ConditionPool.TestPoints` 是否覆盖该检测项样品测点 |
| `TC003` | 检测项缺少 `TestPointParams` | 执行 `CalculateParamAsync` 后再生成 |
| `TC004` | 模板索引未命中任何模板 | 检查模板索引条件是否覆盖该 `TestItemId` / `TestMethod` 组合 |
| `TC005` | 模板缺少对应的 testcondition 文本模板 | 检查模板 `TestConditionTextTemplate` 条件键是否与 `ConditionPool.Conditions` 对齐 |
| `TC007` | dto 条目与清单条目无法唯一对齐 | 看异常 `Detail` 里列出的 dto 实际 `TestItem`/`Samples`，确认前端是否改写了标识 |

> 错误码定义见 `src\Application\Contract\DTOs\TemplateConditionContext\TestConditionDtos.cs`。

---

## 7. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建本 README |
| `bc627f0` | 新增 `CheckListTestParamResolver`：checklist 侧 testParam 从「前端传入的序列化数据」改为「后端查库自算的 testcondition 文本」；同步补齐生成时序约定与错误码表 |
| `bc627f0` | **对齐 bug 修复**：原按 `dto.TestItem == TestItemId.Value` 精确匹配，但前端回传的是展示名称，导致必现失败。改为三级降级：ID 精确 → 名称包含（`NameEn`/`NameChn`，含反向包含）→ `Samples` 集合消歧；均失败抛 `TC007`（禁止位置盲配）。名称字典按需惰性加载（全部按 ID 命中时不查库）。 |
| `bc627f0` | **CuttingMethod 去重生效**：原实现只按换行拆（而实际数据是 `; ` 分隔），导致相同值重复三遍；改为按行 + `;` 双拆后去重，并按原形态回拼。 |
| `bc627f0` | 文件由 `README.md` 更名为 `AGENTS.md`：避免与项目根 README 混淆，且复数形式可被主流 AI 编码工具自动发现并挂载为目录上下文。内容未变更。 |
| `ffa16de` | `ProcessParameterCutting` 新增提取 `Remark`：从 `Parameter` 的 `values` 中读出、以 `Remark=xxx` 追加进 `CuttingMethod`（组内顺序 `SpecimenArea → SpecimenNum → Remark`，多测点组以 `"; "` 连接），并从 `Parameter` 中移除该键；成组判空条件同步扩展为三字段任一非空。仅作用于 `GetCheckListAsync` 展示链路。 |

---

## 8. 未来优化方向

1. **审计模块落表**：当前 `TestConditionCompositionException` 携带 `Code` / `CheckListId` / `TestPoint` / `Detail`，
   仅用于日志。后续审计模块上线后，应把失败原因写入「报错原因表」，便于运维按 Code 聚合统计、快速定位。
2. **对齐策略可配置**：目前固定按 `TestItem` 对齐；若前端允许追加自定义行，需重新定义这些行的 testParam 取值规则。
3. **CuttingMethod 去重已支持 `; ` 与换行两种分隔**（原为待办，已完成）；若前端改用其它分隔符（如逗号），需同步扩展。
4. **时序校验前移**：若希望更早暴露「参数未计算」问题，可把校验前移到 Controller 或过滤器层。
5. **`ProcessParameterCutting` 残留 `Console.WriteLine`**：`catch (JsonException)` 分支里用的是
   `Console.WriteLine`（`CheckListAppService.cs` 内），与项目「统一 `ILogger`」的约定不一致。
   登记待办，本次未改。

---

## 9. 端到端验证记录

### 9.1 已完成的验证（代码侧，代码确证）

| 项 | 方式 | 结果 |
|---|---|---|
| 编译 | `build_solution`（Debug） | 通过，0 error |
| 分层正确性 | 静态核对：`CheckListAdapter` 未引入任何业务依赖；`CheckListGenerateDto` 结构未变 | 通过 |
| 单一实现 | `grep` 确认 `FindByIndex` / `FillTemplate` / `FindTextTemplate` 在生产代码中仅由 `TestConditionComposer` 调用 | 通过 |

### 9.2 需用真实数据人工执行的验证（本步骤无法自动完成）

> 以下四项需要启动服务、连数据库、用真实 checklist 数据执行，故记录为待人工验证项：

1. **形态比对**：生成 `*_CheckList.docx`，与样本
   `wwwroot\DocxModel\SaveDocx\87.405.26.48513.01_261023105345_CheckList.docx` 对比第 4 列：
   - 是否为人类可读的 testcondition 文本（而非参数 JSON）；
   - 多分包是否按行显示（`SetCellText` 按 `\r\n` 拆 Run + Break）；
   - 相同文本是否只出现一次（组内去重）。
2. **一致性比对**：同一 checklist 分别生成 datasheet 与 checklist，两者 testcondition 文本应完全一致
   （证明两条链路共用 `ITestConditionComposer`）。
3. **顺序无关性**：分别以「先 datasheet 后 checklist」与「先 checklist 后 datasheet」各跑一次。
   预期：只要 checklist 生成时四要素齐备，两种顺序结果一致；
   若在参数未计算时生成 checklist，应得到 **TC001/TC003 的明确报错**（这是设计意图，不是缺陷）。
4. **异常路径**：分别构造无 `ConditionPool`、无 `TestPointParams`、模板无文本模板三种数据，
   确认均抛异常且日志包含 `Code` 与原因字段。

### 9.3 已知未覆盖范围

- `CheckListAdapter` 的多 TestGroup 多表场景本次样本只有 1 组，多表渲染未验证；
- `CuttingMethod` 若前端实际以**逗号分隔多值**（而非换行），当前按行去重的实现需相应调整（见 §8 第 3 条）。
