using System.Threading;
using System.Threading.Tasks;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.CheckListContext;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;

namespace NX_lims_Softlines_Command_System.src.Application.Interface
{
    /// <summary>
    /// checklist 侧 testParam 补齐服务。
    ///
    /// 在「生成 checklist docx」之前，按数据库中的条件池与参数集自算出人类可读的
    /// testcondition 文本，并仅覆盖生成 DTO 条目的 Parameter（以及去重后的 CuttingMethod），
    /// 使 <c>CheckListAdapter</c> 保持纯 OpenXML 渲染、无需任何业务依赖。
    ///
    /// 设计约束：
    /// 1. 不改动 <c>CheckListGenerateDto</c> 的结构，前端契约零变更；
    /// 2. 条件不齐备时抛异常阻断生成，禁止静默留空；
    /// 3. 不经过 <c>CheckListAppService.ProcessParameterCutting</c>（该方法服务于
    ///    GetCheckListAsync 的展示链路，会剥离 SpecimenArea/SpecimenNum）。
    /// </summary>
    public interface ICheckListTestParamResolver
    {
        /// <summary>
        /// 补齐并回写 testParam 与 CuttingMethod。
        /// </summary>
        /// <param name="checkList">
        /// 已从库中加载的 checklist 聚合；须包含 Items 且各 Item 的 TestPointParams 已落库。
        /// </param>
        /// <param name="dto">
        /// 前端传入的生成 DTO。方法会就地覆盖其 Items 中对应条目的
        /// <c>Parameter</c> 与 <c>CuttingMethod</c>，其余字段一律不动。
        /// </param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>无返回值；结果通过就地修改 <paramref name="dto"/> 体现。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 条件池缺失（TC001）/ 测点未命中条件池（TC002）/ TestPointParams 缺失（TC003）/
        /// 模板未命中（TC004）/ 模板缺少文本模板（TC005）时抛出，一律阻断生成。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// dto 条目与聚合条目数量不一致，或按 TestItem 找不到对应 dto 条目时抛出
        /// （对齐失败必须显式报错，禁止错位写入）。
        /// </exception>
        Task ApplyAsync(CheckList checkList, CheckListGenerateDto dto, CancellationToken ct);
    }
}
