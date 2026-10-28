# Domain/Contract/Util — 领域契约小类型

> 本文件是该目录的**协作入口文档**。本目录放无需依赖聚合、但被多个聚合/服务共享的轻量契约类型。
>
> 最后更新：基于 `bc627f0`，随 `DataSheetModelGenerator` 领域化重构引入。

---

## 1. 目录内容总览

| 文件 | 类型 | 职责 |
|---|---|---|
| `DataSheetModel.cs` | 模型契约 | 模板填充引擎的输入模型及其两个书签映射子结构 |
| `TestPointPackage.cs` | 参数分包契约 | 一组测点 ID + 对应参数集 |
| `WashDataKind.cs` | 枚举（本次新增） | 区分样本数据 / 洗后数据两套同构规则 |
| `BookmarkMapResult.cs` | 结果契约（本次新增） | 书签映射 + 是否复制整块 data 表的聚合结果 |

---

## 2. 既有类型（代码确证）

### `DataSheetModel.cs`

| 类型 | 行号 | 关键成员 |
|---|---|---|
| `DataSheetModel` | `:3` | `ModelKey`、`ReportNumber`、`TestItem`、`TestMethod`、`TestCondition`、`SheetCount`、`DataAreaCount`、`TemplateUrl`、`SampleMap`、`AfterWashMap` |
| `SampleMap` | `:59` | `Dictionary<string,string> SampleMetaData`、`bool IsAppend` |
| `AfterWashMap` | `:66` | `Dictionary<string,string> AfterWashMetaData`、`bool IsAppend` |

> 三者均为**贫血契约**（自动属性 + 默认值），不含计算逻辑。因此「书签命名与 `IsAppend` 判定」不属于它们，而属于领域服务。

### `TestPointPackage.cs`

| 类型 | 行号 | 成员 |
|---|---|---|
| `TestPointPackage` | `:5` | `IReadOnlyList<string> TestPointIds`、`ParamSet? ParamSet`（构造参数与属性同名） |

---

## 3. 本次新增类型

### `WashDataKind`

```csharp
public enum WashDataKind { Sample = 0, AfterWash = 1 }
```

用途：统一描述「样本数据 / 洗后数据」两套原本逐行重复的展开与书签规则，避免口径分叉。

### `BookmarkMapResult`

```csharp
public sealed class BookmarkMapResult
{
    public IReadOnlyDictionary<string, string> Map { get; }
    public bool IsAppend { get; }
    public static BookmarkMapResult Empty { get; }
}
```

用途：把「书签 → 值」与「是否复制整块 data 表」绑定返回，避免调用方分两次获取导致遍历结果不一致。

**契约**
- `Map` 永不为 null；模板不具备该数据区时为**空字典**；
- `IsAppend` 口径：展开个数 **≥** 模板预留格数；
- 构造函数对 `map` 为 null 抛 `ArgumentNullException`。

**适配责任**：应用层（`DataSheetModelGenerator`）负责把 `BookmarkMapResult` 映射为 `SampleMap` / `AfterWashMap`，本目录不反向依赖应用层。

---

## 4. 修改日志

| 基线 | 变更 |
|---|---|
| `bc627f0` | 新建本 README；新增 `WashDataKind.cs`、`BookmarkMapResult.cs` |
| `bc627f0` | `BookmarkMapResult.IsAppend` 注释按真实实现修正为「容量 > 0 且展开个数**严格大于**容量」（原写「大于或等于」是过期口径）。`WashDataKind` 经实测确认**会影响写入值**（Sample 写测点 ID、AfterWash 写水洗次数），非仅文档口径。 |
| `bc627f0` | 文件由 `README.md` 更名为 `AGENTS.md`：避免与项目根 README 混淆，且复数形式可被主流 AI 编码工具自动发现并挂载为目录上下文。内容未变更。 |
| `bc627f0` | **删除 `SseEmitter.cs`，清除分层违规**：该类持有 `HttpResponse`（ASP.NET Core 类型）却位于 Domain 层，使领域层被 Web 框架绑架。已由 `src\Infrastructure\Realtime\Sse\ISseClient` / `HttpResponseSseClient` 替代（顺带把空实现的 `Complete()` 改为 `CompleteAsync`，并补 `data:` 多行拆分）。删除前全库 grep 确认生产代码**零引用**，仅剩说明性引用（迁移来源注释）。 |

---

## 5. 未来优化方向

1. **`SampleMap` / `AfterWashMap` 结构统一**：两者只差属性名（`SampleMetaData` / `AfterWashMetaData`），可考虑抽象为泛型或公共基类，减少模板引擎侧重复。
2. **`IsAppend` 语义下沉为策略**：若未来出现第三种数据区类型，应把「容量 + 是否复制」抽成统一策略而非继续加枚举分支。
3. **`DataSheetModel` 增加 `SchemaVersion`**：快照 JSON 落库，字段改名会让存量任务反序列化得到 null 值而非异常，建议加版本号支持迁移。
