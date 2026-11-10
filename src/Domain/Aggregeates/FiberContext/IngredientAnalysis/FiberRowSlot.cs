namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis
{
    /// <summary>
    /// 称量行的「**这一行能不能挂子纤维**」闸门 —— 计算与显微镜图名单共用。
    ///
    /// <para>
    /// 两个消费者必须同一口径，否则报告上会出现"有图、结果表里却没有这个成分"：
    /// 结果表按这里的两道闸门展开子行
    /// （<c>IngredientAnalysisCalculation.ExpandedCellulosicSubFibers</c> /
    /// <c>PostProcessBicomponent</c>），图名单按同一套闸门决定要不要插那张图
    /// （<c>FiberWorksheetService.RowFiberNames</c>）。
    /// </para>
    ///
    /// <para>
    /// **比较方式是精确等值**（不 Trim、不忽略大小写），字面量逐字照计算类的原判据 ——
    /// 前端只可能从下拉里选出这两个写法，改名不会命中任何东西，这本身就是想要的。
    /// 别改成 <see cref="FiberTokens.IsCellulosicGroupParent"/>：那个是计数/配对用的口径
    /// （Trim + 忽略大小写），两处语义不同。
    /// </para>
    /// </summary>
    internal static class FiberRowSlot
    {
        /// <summary>这一行是不是分组父槽（报告里按子纤维展开的那两条：普通 / 再生纤维素）。</summary>
        internal static bool IsCellulosicParentSlot(string? fiberName)
            => fiberName is "*cellulosic fibre" or "*Regenerated cellulose fibre";

        /// <summary>这一行是不是复组分行（其下挂两条组分子纤维）。</summary>
        internal static bool IsBicomponentSlot(string? fiberName)
            => fiberName is "Bicomponent Fiber" or "Biconstituent Fiber";
    }
}
