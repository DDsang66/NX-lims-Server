namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.ValueObj
{
    /// <summary>
    /// 纤维项（强类型视图，用于领域计算与测试）
    /// </summary>
    public abstract record CalculatedFiberResult 
    {
        public string Qualitative { get; init; } = string.Empty;
        public string Reagent { get; init; } = string.Empty;
    }


    /// <summary>
    /// 单条纤维项（强类型视图，用于领域计算与测试）
    /// </summary>
    public record SingleCalculatedFiberItem: CalculatedFiberResult
    {
        public string FiberName { get; init; } = string.Empty;
        public string Sample { get; init; } = string.Empty;
        public decimal MoistureRegain { get; init; }
        public decimal Rate { get; init; }
    }
    /// <summary>
    /// 多条纤维项（强类型视图，用于领域计算与测试）
    /// </summary>
    public record MultiCalculatedFiberItem : CalculatedFiberResult
    {
        public string Sample { get; init; } = string.Empty;
        public decimal GSMTrail1 { get; init; }
        public decimal GSMTrail2 { get; init; }
        public decimal RateTrail1 { get; init; } = 100m;
        public decimal RateTrail2 { get; init; } = 100m;
        public decimal Rate { get; init; } = 100m;
        public decimal Avg { get; init; } = 100m;
        public List<MultiFiberRowUnit>? MultiFiberRowUnits { get; init; } = null;
    }

    /// <summary>
    /// 多组分结果单元（强类型视图，用于领域计算与测试）
    /// </summary>
    public record MultiFiberRowUnit
    {
        public string Section { get; init; } = string.Empty;
        public string Sum { get; init; } = string.Empty;
        public decimal GSMTrail1 { get; init; }
        public decimal GSMTrail2 { get; init; }
        public decimal RateTrail1 { get; init; }
        public decimal RateTrail2 { get; init; }
        public decimal Avg { get; init; }
        public decimal Correct { get; init; }

        /// <summary>
        /// 扣掉"下一个成员被 d 放大出来的增量"之后的 <see cref="Avg"/>，**只用来算 Rate、不上报告**。
        /// <c>U_i = Avg_i·Correct_i − Avg_{i+1}·(Correct_{i+1} − 1)</c>，组内最后一个成员不扣。
        /// 为空表示这一行没走扣减链（反序列化 / 直接构造），公式回退到 <c>Avg × Correct</c>。
        /// 见 <c>IngredientAnalysisCalculation.CalculateDissolvedUnits</c>。
        /// </summary>
        public decimal? DeductedAvg { get; init; }

        public decimal MoistureRegain { get; init; }

        /// <summary>
        /// <see cref="MoistureRegain"/> 是不是**纤维表里真有这个数** —— 真值 0 也算有。
        ///
        /// 单开这个标记，是因为报告上「表里就是 0」（如 `Polyurethane` 的 ISO 回潮率 0.00）
        /// 与「根本查不到」（组头行的缩写串 `M/E/S`、表里没有的纤维名、没选标准）
        /// **必须印成两样**：前者印 `0.00%`，后者留空。
        /// 这两件事在 <see cref="MoistureRegain"/> 里都是 `0m`，分不出来。
        ///
        /// 双组分父行恒 false —— 它的加权回潮率只用来算、不上报告，
        /// 别为了"统一"把它改成 true。
        /// </summary>
        public bool MoistureRegainKnown { get; init; }

        public decimal Rate { get; init; }

        /// <summary>
        /// 这一行是**纤维素父槽下插的展示子行** —— 父行那 100% 按子纤维占比切出来的一份。
        ///
        /// 它不是真实成分行：没有自己的称量值（<see cref="GSMTrail1"/> / <see cref="GSMTrail2"/> /
        /// <see cref="RateTrail1"/> / <see cref="RateTrail2"/> / <see cref="Avg"/> /
        /// <see cref="Correct"/> 全空），<see cref="Rate"/> 只是父行 Rate 按
        /// <see cref="CellulosicSubFibers"/> 的占比切出来的，
        /// **不参与** Rate 分母、不进结果表、不进成分数统计、不进称量瓶表。
        /// 见 <c>IngredientAnalysisCalculation.AppendCellulosicSubRows</c>。
        /// </summary>
        public bool IsCellulosicSubRow { get; init; }

        /// <summary>
        /// 这一行是**复合纤维父行下插的展示子行**（<c>Bicomponent Fiber</c> /
        /// <c>Biconstituent Fiber</c> 的两个组分子成分）—— 性质与
        /// <see cref="IsCellulosicSubRow"/> 完全相同：称量列全空，<see cref="Rate"/> 只是父行
        /// Rate 按 <see cref="BicomponentSubFibers"/> 拆干质量切出来的一份，
        /// **不参与** Rate 分母、不进结果表、不进成分数统计、不进称量瓶表。
        /// 见 <c>IngredientAnalysisCalculation.AppendBicomponentSubRows</c>。
        /// </summary>
        public bool IsBicomponentSubRow { get; init; }

        /// <summary>
        /// 是不是展示子行（两种父槽的任意一种）。所有「跳过 / 缩进」的点都判这一个，
        /// 别再各判各的 —— 漏掉一种就会让结果表重复计数、称量瓶多算坩埚。
        /// </summary>
        public bool IsDisplaySubRow => IsCellulosicSubRow || IsBicomponentSubRow;

        public List<CellulosicSubFiber> CellulosicSubFibers { get; init; } = new();
        public List<BicomponentSubFiber> BicomponentSubFibers { get; init; } = new();
    }

}
