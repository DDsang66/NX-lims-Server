using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Util;

namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Service.DataSheetContext
{
    /// <summary>
    /// 数据单模型生成的领域规则契约。
    ///
    /// 本契约只承载跨 <c>CheckListItem</c> / <c>ConditionPool</c> / <c>Template</c>
    /// 三个聚合、且无归属性实体的纯计算规则。
    /// 实现方不得依赖任何仓储或基础设施，须可按纯函数调用。
    /// </summary>
    public interface IDataSheetModelDomainService
    {
        /// <summary>
        /// 按 <c>TestPointParams</c> 的 ParamSet 值分组（分组键剔除 <c>WashCycle</c>），
        /// 输出测点参数分包。
        /// </summary>
        /// <param name="item">检测清单条目，取其 <c>TestPointParams</c>。</param>
        /// <returns>
        /// 按 <c>TestPointIds</c> 首元素升序排序的分包列表；
        /// 无参数配置时返回空列表（非 null）。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> 为 null 时抛出。</exception>
        IReadOnlyList<TestPointPackage> PackByParamSet(CheckListItem item);

        /// <summary>
        /// 计算分包所需的数据区格数。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <param name="package">参数分包。</param>
        /// <returns>数据区格数；返回值小于等于 0 表示模板该数据区不适用。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null 时抛出。</exception>
        int CalculateDataAreaCount(CheckListItem item, TestPointPackage package);

        /// <summary>
        /// 按洗涤次数展开样本数据 / 洗后数据数组。
        /// 返回值长度与 <see cref="CalculateDataAreaCount"/> 的返回值恒等。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <param name="package">参数分包。</param>
        /// <param name="kind">数据种类，决定注释与文档口径。</param>
        /// <returns>展开后的字符串数组。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null 时抛出。</exception>
        string[] ExpandByWashCycle(CheckListItem item, TestPointPackage package, WashDataKind kind);

        /// <summary>
        /// 构建书签映射，并判定是否需要复制整块 data 表。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <param name="package">参数分包。</param>
        /// <param name="template">已选中的模板，提供数据区容量。</param>
        /// <param name="expanded">已展开的数组，直接使用不重复展开。</param>
        /// <param name="kind">数据种类，决定书签前缀与容量来源。</param>
        /// <returns>
        /// 书签映射聚合结果；模板不具备该数据区时返回 <see cref="BookmarkMapResult.Empty"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="template"/> 或 <paramref name="expanded"/> 为 null 时抛出。
        /// </exception>
        BookmarkMapResult BuildBookmarkMap(
            CheckListItem item,
            TestPointPackage package,
            Template template,
            string[] expanded,
            WashDataKind kind);
    }
}
