using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.Enums;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.ValueObj
{
    /// <summary>
    /// 成分分析结果（值对象）
    /// - 保留 Data 作为模板映射的主容器
    /// - 提供常用强类型访问/写入方法与 CalculatedFiberResult 的强类型表示
    /// </summary>
    public record AnalysisResult
    {
        /// <summary>
        /// 字典集合，用于 Word 文档映射（保留，外部模板依赖此结构）
        /// </summary>
        private Dictionary<string, object> Data { get; init; } = new();

        public AnalysisResult() { }

        public AnalysisResult(Dictionary<string, object> data)
        {
            Data = data ?? new Dictionary<string, object>();
        }

        // 便捷构造
        public static AnalysisResult From(Dictionary<string, object> data) => new(data);
        public static AnalysisResult Empty() => new(new Dictionary<string, object>());

        // 通用读取（避免 `as T` 对非引用类型报错）
        public T? Get<T>(string key)
        {
            if (!Data.TryGetValue(key, out var value))
                return default;

            return value is T t ? t : default;
        }

        public object? this[string key] =>
            Data.TryGetValue(key, out var value) ? value : null;

        // --------------------------
        // 强类型辅助访问器（基础字段）
        // --------------------------
        public string ReportNumber => Get<string>("ReportNumber") ?? string.Empty;
        public string Buyer => Get<string>("Buyer") ?? string.Empty;
        public DateTime? CalculateTime => ParseDateTime("CalculateTime");
        public string Methods => Get<string>("Methods") ?? string.Empty;
        public string ComponentType => Get<string>("ComponentType") ?? string.Empty;

        // --------------------------
        // 强类型辅助访问器（标签/备注字段）
        // --------------------------
        public List<string> RecommendedLabel => Get<List<string>>("RecommendedLabel") ?? new List<string>();
        public string RecommendedLabelString => string.Join("/", RecommendedLabel);
        public string ResultRemark => Get<string>("ResultRemark") ?? string.Empty;
        public string LabelRemark => Get<string>("LabelRemark") ?? string.Empty;
        public string JudgmentLabelRemark => Get<string>("JudgmentLabelRemark") ?? string.Empty;
        public string LanguageLabelRemark => Get<string>("LanguageLabelRemark") ?? string.Empty;

        /// <summary>conclusion 段唯一字段。DurabilityLabel/OtherLabel/Comprehensive/FinalResult 已下线。</summary>
        public string VerifyResult => Get<string>("VerifyResult") ?? string.Empty;
        public string BurningTest => Get<string>("BurningTest") ?? string.Empty;
        public List<string> Results => Get<List<string>>("Results") ?? new List<string>();
        public List<string> Recommendation => Get<List<string>>("Recommendation") ?? new List<string>();

        // --------------------------
        // 强类型辅助访问器（设备选型）
        // --------------------------
        public string Equipment_Microscope => Get<string>("Equipment_Microscope") ?? string.Empty;
        public string Equipment_Oven => Get<string>("Equipment_Oven") ?? string.Empty;
        public string Equipment_Balance => Get<string>("Equipment_Balance") ?? string.Empty;
        public string Equipment_WaterBath => Get<string>("Equipment_WaterBath") ?? string.Empty;
        public string Equipment_Shaker => Get<string>("Equipment_Shaker") ?? string.Empty;
        public string Equipment_MicroscopeCellulosic => Get<string>("Equipment_MicroscopeCellulosic") ?? string.Empty;

        // --------------------------
        // 强类型辅助访问器（纤维项）
        // --------------------------
        public int ComponentsCount => ParseInt("ComponentsCount");

        /// <summary>
        /// 强类型的纤维计算结果集合视图
        /// </summary>
        public List<CalculatedFiberResult> CalculatedFiberResult
        {
            get
            {
                if (Data.TryGetValue("CalculatedFiberResult", out var v))
                {
                    if (v is List<CalculatedFiberResult> fi) return fi;
                    if (v is IEnumerable<object> objs)
                    {
                        var list = new List<CalculatedFiberResult>();
                        foreach (var o in objs)
                        {
                            if (o is CalculatedFiberResult f) { list.Add(f); continue; }
                            if (o is Dictionary<string, object> dict)
                            {
                                list.Add(MapDictToCalculatedFiberResult(dict));
                            }
                        }
                        return list;
                    }
                }
                return new List<CalculatedFiberResult>();
            }
        }

        // 辅助：将字典转换为 CalculatedFiberResult（支持 Single 和 Multi）
        private static CalculatedFiberResult MapDictToCalculatedFiberResult(Dictionary<string, object> dict)
        {
            decimal ToDecimal(object? x)
            {
                if (x == null) return 0m;
                if (x is decimal d) return d;
                if (x is double db) return Convert.ToDecimal(db);
                if (x is float f) return Convert.ToDecimal(f);
                if (decimal.TryParse(x.ToString(), out var parsed)) return parsed;
                return 0m;
            }

            var type = dict.TryGetValue("Type", out var t) ? t?.ToString() : string.Empty;

            if (type == AnalysisType.Single.ToString())
            {
                return new SingleCalculatedFiberItem
                {
                    FiberName = dict.TryGetValue("FiberName", out var n) ? n?.ToString() ?? string.Empty : string.Empty,
                    Sample = dict.TryGetValue("Sample", out var s) ? s?.ToString() ?? string.Empty : string.Empty,
                    Qualitative = dict.TryGetValue("Qualitative", out var q) ? q?.ToString() ?? string.Empty : string.Empty,
                    Reagent = dict.TryGetValue("Reagent", out var r) ? r?.ToString() ?? string.Empty : string.Empty,
                    // 单组分没有 GSMTrail1（已退役）。存量字典里若还带这个键，忽略即可。
                    Rate = dict.TryGetValue("Rate", out var rate) ? ToDecimal(rate) : 0m
                };
            }

            // MultiCalculatedFiberItem 需要反序列化 FiberRows
            return new MultiCalculatedFiberItem
            {
                GSMTrail1 = dict.TryGetValue("GSMTrail1", out var mg1) ? ToDecimal(mg1) : 0m,
                GSMTrail2 = dict.TryGetValue("GSMTrail2", out var mg2) ? ToDecimal(mg2) : 0m,
                RateTrail1 = dict.TryGetValue("RateTrail1", out var rt1) ? ToDecimal(rt1) : 100m,
                RateTrail2 = dict.TryGetValue("RateTrail2", out var rt2) ? ToDecimal(rt2) : 100m,
                Rate = dict.TryGetValue("Rate", out var mr) ? ToDecimal(mr) : 100m,
                Avg = dict.TryGetValue("Avg", out var avg) ? ToDecimal(avg) : 100m
                // MultiFiberRowUnits 反序列化较复杂，需要时补充
            };
        }

        // --------------------------
        // 写入辅助：以强类型方式更新 Data
        // --------------------------
        public AnalysisResult WithBasicParams(string reportNumber, string buyer, DateTime calculateTime, IEnumerable<string> methods)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["ReportNumber"] = reportNumber;
            copy["Buyer"] = buyer;
            copy["CalculateTime"] = calculateTime;
            copy["Methods"] = string.Join(new string(' ', 4), methods);
            return new AnalysisResult(copy);
        }

        public AnalysisResult WithComponentType(AnalysisType type)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["ComponentType"] = type.ToString();
            return new AnalysisResult(copy);
        }

        public AnalysisResult WithMethods(string methods)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["Methods"] = methods;
            return new AnalysisResult(copy);
        }

        /// <summary>
        /// 写 docx 书签 <c>BurningTest</c>：每种纤维一段 <c>纤维名: 气味词</c>，逗号分隔、不换行，
        /// 如 <c>Cotton: Burnt paper, Polyester: Faint sweet aroma</c>。气味词见 <see cref="BurningOdour"/>。
        /// </summary>
        public AnalysisResult WithBurningTest(IEnumerable<string> fiberNames)
        {
            var odours = BurningOdour.Describe(fiberNames);
            var copy = new Dictionary<string, object>(Data);
            copy["BurningTest"] = string.Join(", ", odours);
            return new AnalysisResult(copy);
        }

        public AnalysisResult WithAnalysisItems(IEnumerable<CalculatedFiberResult> calculatedFiberResult, int? actualComponentCount = null)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["CalculatedFiberResult"] = calculatedFiberResult.ToList();
            copy["ComponentsCount"] = actualComponentCount ?? calculatedFiberResult.Count();
            return new AnalysisResult(copy);
        }

        public AnalysisResult WithRemarkLabelResult(CalculatedRemarkResult remarkLabel)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["RecommendedLabel"] = remarkLabel.RecommendedLabel;
            copy["ResultRemark"] = remarkLabel.ResultRemark;
            copy["LabelRemark"] = remarkLabel.LabelRemark;
            copy["JudgmentLabelRemark"] = remarkLabel.JudgmentLabelRemark;
            copy["LanguageLabelRemark"] = remarkLabel.LanguageLabelRemark;
            copy["VerifyResult"] = remarkLabel.VerifyResult;
            copy["Results"] = remarkLabel.Results;
            copy["Recommendation"] = remarkLabel.Recommendation;
            return new AnalysisResult(copy);
        }

        /// <summary>
        /// 本份是否出中文（国标）。闸门在 <c>IngredientAnalysisCalculation.CalculateForStandard</c>，
        /// **按段判定** —— 多标准记录里两份各带各的。
        /// </summary>
        public bool UseChineseNames => Get<bool>("UseChineseNames");

        /// <summary>
        /// 纤维英文名 → 中文名。与标准无关，一份实例的各段共用同一张表，缺省是空表。
        /// </summary>
        public IReadOnlyDictionary<string, string> ChineseFiberNames
            => Get<IReadOnlyDictionary<string, string>>("ChineseFiberNames")
               ?? new Dictionary<string, string>();

        /// <summary>
        /// 挂上中文化所需的两个字段。表与布尔一并写入 —— 两者永远同时被读，分两次写会出现
        /// 「有表但开关没打开」的中间态。
        /// </summary>
        public AnalysisResult WithChineseNames(IReadOnlyDictionary<string, string> fiberNames, bool useChinese)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["ChineseFiberNames"] = fiberNames ?? new Dictionary<string, string>();
            copy["UseChineseNames"] = useChinese;
            return new AnalysisResult(copy);
        }

        public AnalysisResult WithEquipment(EquipmentSelection equipment)
        {
            var copy = new Dictionary<string, object>(Data);
            copy["Equipment_Microscope"] = equipment.Microscope;
            copy["Equipment_Oven"] = equipment.Oven;
            copy["Equipment_Balance"] = equipment.Balance;
            copy["Equipment_WaterBath"] = equipment.WaterBath;
            copy["Equipment_Shaker"] = equipment.Shaker;
            copy["Equipment_MicroscopeCellulosic"] = equipment.MicroscopeCellulosic;
            return new AnalysisResult(copy);
        }

        // --------------------------
        // 输出到模板映射器
        // --------------------------
        public Dictionary<string, object> ToDataDictionary()
        {
            var copy = new Dictionary<string, object>(Data);

            // 基础字段
            if (!string.IsNullOrWhiteSpace(ReportNumber)) copy["ReportNumber"] = ReportNumber;
            if (!string.IsNullOrWhiteSpace(Buyer)) copy["Buyer"] = Buyer;
            if (CalculateTime.HasValue) copy["CalculateTime"] = CalculateTime.Value;
            if (!string.IsNullOrWhiteSpace(Methods)) copy["Methods"] = Methods;
            if (!string.IsNullOrWhiteSpace(ComponentType)) copy["ComponentType"] = ComponentType;

            // 纤维项
            if (CalculatedFiberResult?.Any() == true) copy["CalculatedFiberResult"] = CalculatedFiberResult;
            copy["ComponentsCount"] = ComponentsCount;

            // 标签/备注字段
            if (RecommendedLabel?.Any() == true) copy["RecommendedLabel"] = RecommendedLabel;
            if (!string.IsNullOrWhiteSpace(ResultRemark)) copy["ResultRemark"] = ResultRemark;
            if (!string.IsNullOrWhiteSpace(LabelRemark)) copy["LabelRemark"] = LabelRemark;
            if (!string.IsNullOrWhiteSpace(JudgmentLabelRemark)) copy["JudgmentLabelRemark"] = JudgmentLabelRemark;
            if (!string.IsNullOrWhiteSpace(LanguageLabelRemark)) copy["LanguageLabelRemark"] = LanguageLabelRemark;
            if (!string.IsNullOrWhiteSpace(VerifyResult)) copy["VerifyResult"] = VerifyResult;
            if (Results?.Any() == true) copy["Results"] = Results;
            if (Recommendation?.Any() == true) copy["Recommendation"] = Recommendation;

            return copy;
        }

        // --------------------------
        // 辅助解析方法
        // --------------------------
        private DateTime? ParseDateTime(string key)
        {
            if (!Data.TryGetValue(key, out var v)) return null;
            if (v is DateTime dt) return dt;
            if (v is string s && DateTime.TryParse(s, out var parsed)) return parsed;
            return null;
        }

        private int ParseInt(string key)
        {
            if (!Data.TryGetValue(key, out var v)) return 0;
            if (v is int i) return i;
            if (int.TryParse(v?.ToString(), out var parsed)) return parsed;
            return 0;
        }
    }
}
