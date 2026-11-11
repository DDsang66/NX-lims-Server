namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis
{
    /// <summary>
    /// 纤维模块录入界面的**分组候选清单**。
    ///
    /// 原先散在前端三处内联，且互相不一致：cellulosic 子纤维是全小写
    /// 'hemp'/'cotton'/'linen'/'ramie'（与 fiber_database 的拼写不符），
    /// bicomponent 子纤维是 ['Polyester','Polyamide']，而标准清单是 10 条硬编码。
    ///
    /// **为什么放在 Domain、与 <see cref="FiberTokens"/> 同层**：这三张表回答的是
    /// "哪些纤维属于哪个分组"，与谓词表是**同一类领域知识** —— 谓词表喂配对规则、
    /// 这张表喂录入界面，两者若不一致就会出现"界面能选、规则不认"的组合。
    /// 放在同一目录是为了让"分组归属"只有一个出处；前端只管渲染拿到的清单。
    ///
    /// **与谓词表的一致性由 FiberOptionsTests 逐名比对**，不靠人记得同步。
    /// 注意这里写的是**显示名**（首字母大写，与 fiber_database.fiber_name_en 一致），
    /// 而 <see cref="FiberTokens"/> 的集合全是小写字面量（其入口自带 Trim + 大小写不敏感）。
    ///
    /// **刻意不收**：分组父槽字面量本身（*cellulosic fibre 之类 —— 它们是"分组"不是"纤维"，
    /// 不该出现在子纤维下拉里）。
    ///
    /// **成员取自标准的官方范围，不是"生产里出现过就收"**。天然纤维素那份是
    /// ISO 1833-11:2017 的对象纤维（cotton / flax / hemp / ramie）再加 Linen ——
    /// 后者与 Flax 是同一纤维在 fiber_database 里的两行，本模块一直按同义词处理。
    /// 黄麻（jute）虽是纤维素纤维，但 ISO 1833 系列没有它的分部，
    /// <see cref="FiberTokens.IsCellulosicOrCotton"/> 也刻意不收，故这里同样不给。
    /// </summary>
    internal static class FiberOptions
    {
        /// <summary>
        /// *cellulosic fibre（**天然**纤维素父槽）下的子纤维候选 ——
        /// ISO 1833-11:2017 的对象纤维（cotton / flax / hemp / ramie）加 Linen（同义词）。
        /// 原名硬编码小写，是生产库里出现小写 linen(18 条)/hemp(2 条) 的根因。
        /// </summary>
        internal static readonly IReadOnlyList<string> CellulosicSub = new[]
        {
            "Cotton", "Flax", "Hemp", "Linen", "Ramie",
        };

        /// <summary>
        /// *Regenerated cellulose fibre（**再生**纤维素父槽）下的子纤维候选。
        ///
        /// 与 <see cref="FiberTokens.RayonType"/> 同集合（去父槽字面量、改显示名）。
        /// **此前两个父槽共用同一份 4 个天然纤维素候选**，于是"再生纤维素"下选不到
        /// Viscose/Lyocell/Modal，反而能选到不该有的 Hemp —— 那份清单本是为天然那个父槽写的。
        /// </summary>
        internal static readonly IReadOnlyList<string> RegeneratedSub = new[]
        {
            "Cupro", "Lyocell", "Modal", "Rayon", "Viscose",
        };

        /// <summary>
        /// Bicomponent Fiber / Biconstituent Fiber 两个占位行下的子纤维候选。
        /// 这两个子纤维名是**双组分复合纤维的组成**，与上面的分组父槽不同：
        /// 它们不是"某一类纤维的成员"，只是这两个占位行内部拆百分比用。
        ///
        /// **Nylon 与 Polyamide 并列**：两者是 fiber_database 里
        /// 同一个锦纶的两行，各列数值逐项相同（ISO 5.75 / AATCC 4.50 / GB 4.50，均已启用），
        /// 差别只在叫法 —— AATCC 口径下分析员录 Nylon，ISO/FZ 口径下录 Polyamide。
        /// 两个父行共用这一份清单，所以四个组合都能选到。
        ///
        /// 另有 **ISO 专用那两条**（`Nylon (ISO 6.25%)` / `Polyamide (ISO 6.25%)`，
        /// 只填 Iso 列、回潮率 6.25，见 <see cref="FiberTokens.IsIsoPolyamideVariant"/>）：
        /// 它们是下拉层面的条目，**不进这份子纤维清单** —— 复合纤维的组成按上面三条录。
        ///
        /// **加它对计算是零漂移**：<c>LookupMoistureRegain</c> 两行同值（无论按哪个标准的列）、
        /// <c>BurningOdour</c> 两行同为 Amide、显微镜取图本就有 Nylon→Polyamide 的别名
        /// （见 <see cref="MicroscopeImageName"/>）。会变的只是报告上印出来的名字。
        /// </summary>
        internal static readonly IReadOnlyList<string> BicomponentSub = new[]
        {
            "Polyester", "Polyamide", "Nylon",
        };
    }
}
