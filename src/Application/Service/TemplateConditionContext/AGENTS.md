# Application/Service/TemplateConditionContext — testcondition 文本合成

> 本文件是该目录的**协作入口文档**。接手前请先读完本文档，即可掌握：
> 本目录解决什么问题、被谁调用、两条铁律、错误码与排查动作。
>
> 最后更新：基于 `bc627f0`，checklist testParam 改造引入。

---

## 1. 一句话定位

**把「条件池 + 参数集 + 检测项」合成为人类可读的试验条件文本（testcondition），供 datasheet 与 checklist 两条链路共用同一份实现。**

之所以需要这份公共实现：原先「运行时条件组装 → 模板索引匹配 → 文本模板填充」三段逻辑内联在
`DataSheetModelGenerator.GenerateAsync` 方法体内（改造前位于 `DataSheetModelGenerator.cs:59-162`），
没有任何独立方法。checklist 侧要复用就得照抄一份，两份实现必然分叉——并且极易抄错下面第 3 节的两条铁律。

---

## 2. 目录内容总览

| 文件 | 类型 | 职责 |
|---|---|---|
| `TestConditionComposer.cs` | 应用服务（`IScopedDependency`） | 唯一实现：运行时条件组装、模板选择、文本填充、分包合成与去重 |
| `src\Application\Interface\ITestConditionComposer.cs` | 契约（接口） | 5 个方法，见第 4 节契约表 |
| `src\Application\Contract\DTOs\TemplateConditionContext\TestConditionDtos.cs` | 契约（DTO/异常） | `TestConditionText`、`TestConditionComposition`、`TestConditionErrorCodes`、`TestConditionCompositionException` |

---

## 3. 两条铁律（改动前必读）

### 铁律 1：两处用的**不是同一个字典**

| 环节 | 使用哪个字典 | 依据 |
|---|---|---|
| 模板索引匹配 `FindByIndex` | **运行时条件**（克隆 `pool.Conditions` + 合并 `ParamSet.Values` + 追加 `TestItemId`/`TestMethod`） | `DataSheetModelGenerator.cs:131`（改造前） |
| 文本模板匹配 `FindTextTemplate` | **原始 `pool.Conditions`** | `DataSheetModelGenerator.cs:160`（改造前） |

因此 `ComposeText(Template, ConditionPool, ParamSet?)` 的签名**强制同时提供**模板与条件池，
调用方无法只传运行时条件字典——这是刻意的类型级约束，不要为了"顺手"改成单参重载。

用错的表现是**静默错文本**：不报错，但 `TestCondition` 会取到另一段或空文本。

### 铁律 2：模板索引初筛键固定为 `"TestItemId"`

`ResolveTemplate` 内部固定传 `preFilterKey: "TestItemId"`，不暴露为参数。
这是模板索引的初筛口径，任何调用场景都不应改变；确需变化时必须先评估对现有模板索引的影响。

### 补充：`TestMethod` 必须先排序

`BuildRuntimeConditionsAsync` 会把 `TestMethod` 写成 `OrderBy(s => s.Id.Value, OrdinalIgnoreCase)` 拼接的结果。
原因是 `TestMethod` 会进入模板索引的 `MatchesAll` **逐字比对**，依赖数据库返回顺序会让同一组标准
拼出 `"A;B"` 或 `"B;A"`，造成索引匹配漂移。这条注释在代码里保留，请勿删除。

---

## 4. 方法契约

| 方法 | 入参 | 返回 | 抛异常 |
|---|---|---|---|
| `BuildRuntimeConditionsAsync` | `ConditionPool pool`、`ParamSet? paramSet`、`CheckListItem item`、`CancellationToken ct` | `Task<IReadOnlyDictionary<string, object?>>`，含 `TestItemId`、`TestMethod`；**当且仅当该检测项恰好有 1 个标准时**，额外含 `StandardMethod` | `ArgumentNullException`（pool/item 为 null）；`TestConditionCompositionException`（标准库查询导致 TestMethod 无法确定） |
| `ResolveTemplate` | `IReadOnlyDictionary<string, object?> runtimeConditions` | `Template`（**永不为 null**） | `ArgumentNullException`；`TestConditionCompositionException`（**TC004** 无匹配模板） |
| `ComposeText` | `Template template`、`ConditionPool pool`、`ParamSet? paramSet` | `string` 填充完成的文本；**`TestConditionCount <= 0` 时返回 `string.Empty`** | `ArgumentNullException`；`TestConditionCompositionException`（**TC005** 模板无对应文本模板，仅在 `TestConditionCount > 0` 或结构为 null 时可能抛出） |
| `ComposeForPackageAsync` | `CheckListItem item`、`ConditionPool pool`、`TestPointPackage package`、`CancellationToken ct` | `Task<TestConditionComposition>`（Text + Template + RuntimeConditions） | 同上述 TC004 / TC005 |
| `ComposeForItemAsync` | `CheckListItem item`、`ConditionPool pool`、`CancellationToken ct` | `Task<TestConditionText>`（Text + Lines + PackageCount） | **TC003**（无 TestPointParams）；TC004 / TC005 |

### `ComposeText` 的分流规则（`TestConditionCount`）

`ComposeText` 在匹配文本模板**之前**，先看模板结构声明的条件位置数：

| `TemplateStructure.TestConditionCount` | 行为 |
|---|---|
| `<= 0`（含 0） | 直接返回 `string.Empty`，**不抛 TC005** |
| `> 0` | 走原逻辑：必须找到文本模板（否则 TC005），再填充 |
| `TemplateStructure == null` | 按「需要」处理，继续走 TC005（保守） |

**为什么与发布校验同口径**：`WordTemplateValidator.cs:88` 也只有 `TestConditionCount > 0` 才要求 docx 含 `TestCondition` 书签。此前合成器不读该字段、一律强制要求文本模板，会让「声明不需要 testcondition」的模板**直接生成失败**（隐性故障）。

**为什么返回空串是安全的**：`DataSheetFillingEngine.ReplaceBookmarks`（`:560-563`）是按 docx 里**实际存在的书签**去匹配字典的，模板没埋 `TestCondition` 签名时，字典里的空值不会写入任何内容。

**两个已知边界**：
1. `TemplateStructure == null` 属历史脏数据（`Publish` 已要求非空，且 `FindByIndex` 只返回 Published），按「需要」处理，宁可报错不静默跳过。
2. `TestConditionCount` 实际只被当**布尔**用：校验器只要求**单个**（不编号）`TestCondition` 书签，`== 1` 与 `== 5` 行为完全相同。字段名是「个数」，但它不表达「模板里有 N 个条件位置」。

### `ComposeForItemAsync` 的行为要点

1. 分包交给领域服务 `DataSheetModelDomainService.PackByParamSet`（零仓储依赖，与 datasheet 侧同一份实现）；
2. **每个分包输出两行**：先一行「测点组标签」`[{TestPoint1/TestPoint2}]:`，再一行条件文本。
   标签必须保留——纸质清单上读者要靠它判断该段条件属于哪个样品；缺标签等于丢失信息。
3. **条件文本为空的测点组整段跳过**：不输出标签行。只留 `[A]:` 而不填内容比留白更难看，
   也容易让人误以为该测点漏算了参数。若所有分包都为空，返回 `string.Empty`，单元格整格留白。
4. **组内去重键 = 标签 + 文本整体**：测点组合与条件都相同才合并（对齐既有样本形态）。
   若只按文本去重，两组测点共用同一段条件时会被误合并成一段，丢掉测点归属。
5. **段与段之间空一行**（`\r\n\r\n`）：多个测点组在单元格里视觉分开、更易读；
   **整格末尾不额外留空行**——那只撑高行高，不增加可读性。
   渲染由 `CheckListAdapter.SetCellText` 完成（按 CRLF 拆成 Run + Break），Adapter 不做排版补偿。

### `StandardMethod` 条件键的注入规则

`BuildRuntimeConditionsAsync` 会额外注入一个 `StandardMethod` 键（如 `ISO 12945-2`），由
`IStandardMethodCodeResolver`（`src\Domain\Services\StandardContext`）把具体标准号归一化得到，
目的是让模板能按「测试方法」而不是「具体标准号」绑定，从而兼容 `ISO` / `EN ISO` / `BS EN ISO` 等写法变体。

| 检测项标准数 | 是否注入 | 理由 |
|---|---|---|
| 0 | 否 | 无条件可用 |
| **恰好 1** | **是**（解析成功时） | 方法键无歧义 |
| 多个 | **否** | 多标准拼成什么键尚无业务共识；强行拼接会重新引入「顺序敏感 + 组合爆炸」，与本次要解决的问题同类。此时模板退化为只用 `TestMethod` 匹配 |

**性能约束**：`TestMethod` 与 `StandardMethod` 必须共用**同一次** `GetByIdsAsync` 结果
（`LoadStandardsAsync` + `BuildTestMethodString` 拆分正是为此），**禁止分两次查库**——
那会让每个参数包多一次 DB 往返。

**待确认项**：多标准场景下的方法键口径（是否拼接、如何拼接、是否取交集）尚未决策，
当前保守选择「不注入」。待真实业务出现「一个检测项对应多标准且需要按方法匹配模板」时再定。

---

## 5. 错误码表（审计模块按此分类）

| Code | 含义 | 排查动作 |
|---|---|---|
| `TC001` | 未找到该 checklist 的条件池 | 先完成参数计算，使 `ConditionPool` 落库 |
| `TC002` | 测点未在条件池中命中 | 检查 `ConditionPool.TestPoints` 是否包含该测点 |
| `TC003` | 检测项缺少 `TestPointParams` | 执行 `CalculateParamAsync` 后再生成 checklist |
| `TC004` | 模板索引未命中任何模板 | 检查模板索引条件是否覆盖该 `TestItemId` / `TestMethod` 组合；确认模板已发布 |
| `TC005` | 模板缺少对应的 testcondition 文本模板（**仅在 `TestConditionCount > 0` 或 `TemplateStructure` 为 null 时可能抛出**） | 检查模板 `TestConditionTextTemplate` 的条件键是否与 `ConditionPool.Conditions` 对齐 |
| `TC006` | 模板 URL 为空 | 检查 `DataSheet.BindTemplate` 是否已回写模板 URL |
| `TC007` | dto 条目与清单条目无法唯一对齐 | 由 `CheckListTestParamResolver` 抛出；看异常 `Detail` 里列出的 dto 实际值 |

> 约定：错误一律**抛异常阻断**，不静默留空。异常携带 `Code` / `CheckListId` / `TestPoint` / `Detail`，
> 上层当前用于结构化日志，后续审计模块可替换为写「报错原因表」。

---

## 6. 被调用方清单

| 调用方 | 使用的方法 | 说明 |
|---|---|---|
| `src\Application\Service\DataSheetContext\DataSheetModelGenerator.cs` | `ComposeForPackageAsync` | datasheet 侧：替换原先内联的三段逻辑 |
| `src\Application\Service\CheckListContext\CheckListTestParamResolver.cs` | `ComposeForItemAsync` | checklist 侧：合成 testParam 单元格文本 |

> 口径单一：datasheet 与 checklist 的 testcondition 文本必须由**同一个** `ITestConditionComposer` 产出。
> 任何一侧出现"本地再算一遍"的实现，即视为回归缺陷。

---

## 7. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建目录与公共组件；从 `DataSheetModelGenerator.GenerateAsync` 抽取 runtimeConditions 组装 / `FindByIndex` / `FindTextTemplate` + `FillTemplate` 三段逻辑，改为单一实现 |
| `bc627f0` | `ComposeForItemAsync` 输出补「测点组标签」（`[A]:` / `[B/C-body/C-bottom]:`），去重键改为标签+文本整体。原因：对比旧样本 docx 发现缺标签后无法判断条件属于哪个测点，「即读即用」目标未达成 |
| `bc627f0` | 文件由 `README.md` 更名为 `AGENTS.md`：避免与项目根 README 混淆，且复数形式可被主流 AI 编码工具自动发现并挂载为目录上下文。内容未变更。 |
| `ffa16de` | `ComposeText` 新增 `TestConditionCount` 分流：模板结构声明 `TestConditionCount <= 0` 时直接返回 `string.Empty`、不抛 TC005，口径与 `WordTemplateValidator.cs:88` 对齐（该字段此前只有校验器在用，合成器不读，导致「声明不需要 testcondition」的模板生成失败）。结构为 null 时仍走 TC005。 |
| `ffa16de` | `ComposeForItemAsync` 输出格式调整：① 条件文本为空的测点组**整段跳过**（不再出现孤立的 `[A]:` 空标签）；② 段间连接符由 `\r\n` 改为 `\r\n\r\n`（段间空行、末尾不留），提升纸质清单可读性。全部为空时返回 `string.Empty`，单元格整格留白。 |

---

## 8. 未来优化方向

1. **模板匹配缓存**：`ComposeForItemAsync` 内每个分包都会调一次 `FindByIndex`（可能查库）。若同一检测项的多个分包条件相同，可缓存 `Template` 减少查询——功能优先，暂不实现，代码中已留 `// TODO(封装)`。
2. **错误码细分**：`TC005` 目前合并了「模板未配置文本模板」与「条件键不匹配」两种情况；待审计模块上线、按真实故障分布统计后再拆分。
3. **序列化配置统一**：本类用 `System.Text.Json` + `TypeConverter`；分组键序列化仍在领域服务用 Newtonsoft。统一前需先补分组键稳定性测试。
4. **可配置化**：`preFilterKey` 目前硬编码 `"TestItemId"`，若未来出现多套索引口径，再考虑抽为策略。

---

## 9. 验证记录

| 项 | 方式 | 结果 |
|---|---|---|
| 编译 | `build_solution`（Debug） | 通过，0 error |
| 调用方收敛 | `grep` 确认 `FindByIndex` / `FindTextTemplate` / `TextReplaceHelper.FillTemplate` 在生产代码中仅本组件调用 | 通过（datasheet 侧已改为调用本组件，checklist 侧经 `CheckListTestParamResolver` 间接调用） |
| 错误码抛出点 | 静态核对：TC004 在 `ResolveTemplate`、TC005 在 `ComposeText`、TC003 在 `ComposeForItemAsync` | 通过 |
| 异常路径实测 | 需真实数据构造（见 `CheckListContext\AGENTS.md` §9.2） | 待人工执行 |

> 说明：本组件本身是纯计算 + 两次仓储/服务调用，不具备独立 E2E 入口；
> 端到端验证请参照 `CheckListContext\AGENTS.md` §9.2 的四项清单。
