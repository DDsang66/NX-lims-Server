using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Util;

namespace NX_lims_Softlines_Command_System.src.Application.Interface
{
    /// <summary>
    /// testcondition 文本合成器（公共组件）。
    ///
    /// 把「条件池 + 参数集 + 检测项」合成为人类可读的试验条件文本，
    /// 供 datasheet（DataSheetModelGenerator）与 checklist（CheckListTestParamResolver）
    /// 两条链路共用，保证「条件组装 / 模板索引匹配 / 文本模板填充」三段逻辑只有一份实现。
    ///
    /// 两条铁律（改动前必读）：
    /// 1. <see cref="ComposeText"/> 的文本模板匹配必须用 <c>pool.Conditions</c>（原始条件），
    ///    不能用运行时条件字典 —— 故签名强制同时提供两者，不允许调用方替换。
    /// 2. 模板索引初筛键固定为 <c>"TestItemId"</c>，不随调用方变化，故不暴露为参数。
    /// </summary>
    public interface ITestConditionComposer
    {
        /// <summary>
        /// 组装用于模板索引匹配的「运行时条件字典」。
        /// </summary>
        /// <param name="pool">条件池实体，提供基础 Conditions（内部会克隆，不修改原实体）。</param>
        /// <param name="paramSet">本分包的参数集，其 Values 会覆盖同名键；为 null 时不合并。</param>
        /// <param name="item">检测项，提供 TestItemId 与 StandardIds。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>只读运行时条件字典，至少包含 TestItemId 与 TestMethod。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 标准库查询等导致 TestMethod 无法确定时抛出。
        /// </exception>
        Task<IReadOnlyDictionary<string, object?>> BuildRuntimeConditionsAsync(
            ConditionPool pool,
            ParamSet? paramSet,
            CheckListItem item,
            CancellationToken ct);

        /// <summary>
        /// 按运行时条件选出模板。
        /// </summary>
        /// <param name="runtimeConditions">由 <see cref="BuildRuntimeConditionsAsync"/> 产出的字典。</param>
        /// <returns>命中的模板实体，永不为 null。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 错误码 TC004：模板索引未命中任何模板。
        /// </exception>
        Template ResolveTemplate(IReadOnlyDictionary<string, object?> runtimeConditions);

        /// <summary>
        /// 合成单个分包的 testcondition 文本。
        /// </summary>
        /// <param name="template">已选中的模板。</param>
        /// <param name="pool">
        /// 条件池实体。<b>文本模板匹配必须使用其 Conditions（原始条件）</b>，
        /// 不能改用运行时条件字典。
        /// </param>
        /// <param name="paramSet">本分包的参数集，序列化为 paramJson 后填充文本模板占位符。</param>
        /// <returns>填充完成的 testcondition 文本。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 错误码 TC005：模板中找不到与该条件对应的文本模板。
        /// </exception>
        string ComposeText(Template template, ConditionPool pool, ParamSet? paramSet);

        /// <summary>
        /// datasheet 侧主入口：合成单个分包，同时返回命中的模板与运行时条件。
        /// </summary>
        /// <param name="item">检测项。</param>
        /// <param name="pool">条件池。</param>
        /// <param name="package">本分包（来自 PackByParamSet 的结果）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>分包合成结果，含 Template 与 RuntimeConditions 便于调用方复用。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 错误码 TC004 / TC005。
        /// </exception>
        Task<TestConditionComposition> ComposeForPackageAsync(
            CheckListItem item,
            ConditionPool pool,
            TestPointPackage package,
            CancellationToken ct);

        /// <summary>
        /// checklist 侧主入口：对单个检测项完成「分包 → 逐包合成 → 组内去重」。
        /// </summary>
        /// <param name="item">检测项（需带 TestPointParams）。</param>
        /// <param name="pool">该检测项命中的条件池。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>去重后的分行文本；PackageCount 为去重前的分包数。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 错误码 TC003（无 TestPointParams）或 TC004 / TC005（模板相关）。
        /// </exception>
        Task<TestConditionText> ComposeForItemAsync(
            CheckListItem item,
            ConditionPool pool,
            CancellationToken ct);
    }
}
