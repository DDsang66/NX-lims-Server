using NX_lims_Softlines_Command_System.src.Application.Interface.FiberTeamContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Data.Repository;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.TemplateEngine.WordTemplateAdapter
{
    public class FiberAnalysisWordTemplateAdapter : IWordTemplateAdapter, IScopedDependency
    {
        /// <summary>
        /// 从 IngredientAnalysis 计算结果中重构 Data，
        /// 拍平为模板可直接使用的 Dictionary<string, string>
        /// 数组/嵌套结构留空，后续单独处理
        /// </summary>
        public (Dictionary<string, string> Values, HashSet<string> RedBookmarks, HashSet<string> RemoveWhenEmpty, HashSet<string> RemoveBlockWhenEmpty) Adapt(AnalysisResult analysisResult)
        {
            var flatData = new Dictionary<string, string>();
            var Data = new Dictionary<string, string>();
            var redBookmarks = new HashSet<string>();
            var removeWhenEmpty = new HashSet<string>();
            var removeBlockWhenEmpty = new HashSet<string>();

            // 本报告里所有"随机"占位值的共同种子。取一次、下面各 random 各用各的实例 ——
            // 实例是独立的，消费节奏也各不相同，不会互相干扰。
            int seed = StableSeed(analysisResult.ReportNumber);

            // 基础字段（直接映射）
            flatData["ReportNumber"] = analysisResult.ReportNumber;
            flatData["Buyer"] = analysisResult.Buyer;
            flatData["CalculateTime"] = analysisResult.CalculateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
            flatData["Methods"] = analysisResult.Methods;
            flatData["ComponentType"] = analysisResult.ComponentType;

            // 标签/备注字段（直接映射）
            // 选空时清空 Recommend 单元格，选 Yes 时不填保持原样
            if (string.IsNullOrWhiteSpace(analysisResult.RecommendedLabelString))
            {
                flatData["Recommend"] = "";
                removeWhenEmpty.Add("Recommend");
            }

            flatData["ResultRemark"] = analysisResult.ResultRemark;
            flatData["LabelRemark"] = analysisResult.LabelRemark;
            flatData["JudgmentLabelRemark"] = analysisResult.JudgmentLabelRemark;
            flatData["LanguageLabelRemark"] = analysisResult.LanguageLabelRemark;
            flatData["VertifyResult"] = analysisResult.VerifyResult;  // 模板书签名为 VertifyResult

            // Conclusion / Remark 两块没内容就整块不显示。
            //
            // 这两块在模板里各是一张无边框的表，标签 "Conclusion:" / "Remark:" 是表里的
            // 普通文字、不是书签 —— 只清空书签值或只删标签都会剩一张空表占版面。
            // 所以交给引擎的 removeBlockWhenEmpty：值为空时把**整张表**连表带间距一起摘掉。
            //
            // 判据按"这块到底有没有东西"来：
            //   Conclusion ← VerifyResult（结论表里就这一个值）
            //   Remark     ← 四个备注字段任一非空就算有用（Remark 表有三行，任一行有字都要留整表）
            //
            // 有内容时**一个字都不碰** —— 不进 removeBlockWhenEmpty 也不动 flatData，
            // 表格因此逐字节不变（报告开头那段的排版不会因为这次改动漂移）。
            if (string.IsNullOrWhiteSpace(analysisResult.VerifyResult))
            {
                flatData["Conclusion"] = "";
                removeBlockWhenEmpty.Add("Conclusion");
            }

            if (string.IsNullOrWhiteSpace(analysisResult.ResultRemark)
                && string.IsNullOrWhiteSpace(analysisResult.LabelRemark)
                && string.IsNullOrWhiteSpace(analysisResult.JudgmentLabelRemark)
                && string.IsNullOrWhiteSpace(analysisResult.LanguageLabelRemark))
            {
                flatData["Remark"] = "";
                removeBlockWhenEmpty.Add("Remark");
            }
            // 模板 conclusion 段删行后，DurabilityLabel/OtherLabel/Comprehensive/FinalResult
            // 四个书签已不存在，对应的 flatData 一并去掉（无书签的值本来也会被 ReplaceText 静默跳过）
            flatData["BurningTest"] = analysisResult.BurningTest;

            // 数组/嵌套结构：留空，后续单独处理
            // 领域层给的是整行 `65.0% Cotton` / 单组分 `a:  100% Modal`，这里按第一个 % 切开。
            var results = analysisResult.Results;
            for (int i = 0; i < results.Count; i++)
            {
                var (percent, name) = SplitFiberResultLine(results[i]);
                flatData[$"TestResultper_{i + 1}"] = percent;
                flatData[$"TestResult_{i + 1}"] = name;
            }

            // 数组展开：Recommendation → Recommendationper_/Recommendation_ 两列
            // 当推荐标签为空时跳过填入
            if (!string.IsNullOrWhiteSpace(analysisResult.RecommendedLabelString))
            {
                var recommendations = analysisResult.Recommendation;
                for (int i = 0; i < recommendations.Count; i++)
                {
                    var (percent, name) = SplitFiberResultLine(recommendations[i]);
                    flatData[$"Recommendationper_{i + 1}"] = percent;
                    flatData[$"Recommendation_{i + 1}"] = name;
                }
            }
            // flatData["CalculatedFiberResult"] = ?;  // 复杂对象列表，需逐行展开
            var fiberData = ExpandCalculatedFiberResult(
                analysisResult.CalculatedFiberResult, redBookmarks, seed);
            foreach (var kv in fiberData)
            {
                flatData[kv.Key] = kv.Value;
            }


            // 设备选型字段 — 拼接所有非空设备值
            var equipmentParts = new[]
            {
                analysisResult.Equipment_Microscope,
                analysisResult.Equipment_Oven,
                analysisResult.Equipment_Balance,
                analysisResult.Equipment_WaterBath,
                analysisResult.Equipment_Shaker
            }.Where(e => !string.IsNullOrWhiteSpace(e));
            flatData["Equipment"] = string.Join("    ", equipmentParts);

            // 页脚 MR 汇总。
            // 判据与 MR 栏那一列同一套（MoistureRegainKnown，不是 > 0）—— 免得出现
            // "表里印了 0.00%、页脚却不列这一条"的自相矛盾。
            // 模板侧只有 **Multi** 那份有 MR 书签（footer1.xml 的 `Moisture regain :` 之后），
            // Single 那份的 footer 里没有，所以这句只在多组分报告上印得出来。
            //
            // 取位与 MR **栏**那一列刻意不同：栏里恒两位（0.00 / 8.50），页脚第二位小数为 0 时只留一位
            // （8.50 → 8.5、4.00 → 4.0），有值才印两位（5.75）—— 见 FormatFooterMoistureRegain。
            // 欧洲（ISO1833 / DIN / EN / BS）与澳洲（AS 2001.7）体系恒两位小数。
            //
            // 同名只留第一条。
            // 去重不会改到值：能走到这里行的回潮率都是按名字查表来的（双组分父行已被上面
            // 那道 MoistureRegainKnown 排除），同名必然同值。
            // 国标（FZ/T 01057）时纤维名带中文，与 Test Result 那一列同一套取名口。
            // 去重仍在**英文原名**上做（DistinctBy 排在取名之前）—— 反过来的话，
            // 同一只纤维在表里出现两种写法就会重复列一条。
            var useChinese = analysisResult.UseChineseNames;
            var keepTwoDecimals = MoistureRegainResolver.UsesIsoSchedule(analysisResult.Methods);
            var mrItems = analysisResult.CalculatedFiberResult
                .OfType<MultiCalculatedFiberItem>()
                .SelectMany(m => m.MultiFiberRowUnits ?? new List<MultiFiberRowUnit>())
                .Where(r => !string.IsNullOrWhiteSpace(r.Sum) && !r.Sum.Contains('/'))
                .Where(r => r.MoistureRegainKnown)
                .DistinctBy(r => r.Sum)
                .Select(r => $"{FiberChineseName.Localize(r.Sum, useChinese ? analysisResult.ChineseFiberNames : null)} {FormatFooterMoistureRegain(r.MoistureRegain, keepTwoDecimals)}%");
            flatData["MR"] = string.Join("  ", mrItems);

            // 计数字段
            flatData["ComponentsCount"] = analysisResult.ComponentsCount.ToString();

            // 保存到工作单
            foreach (var kv in flatData)
            {
                Data[kv.Key] = kv.Value;
            }

            var values = Data.ToDictionary(
                kv => kv.Key,
                kv => kv.Value?.ToString() ?? string.Empty);
            return (values, redBookmarks, removeWhenEmpty, removeBlockWhenEmpty);
        }

        /// <summary>
        /// 展开 CalculatedFiberResult 到扁平字典
        /// </summary>
        /// <param name="seed">由报告号派生的随机种子，透传给瓶号/称量瓶两处占位值。</param>
        private Dictionary<string, string> ExpandCalculatedFiberResult(
            List<CalculatedFiberResult> calculatedFiberResult, HashSet<string> redBookmarks, int seed)
        {
            var flatData = new Dictionary<string, string>();

            int itemIndex = 1;

            foreach (var item in calculatedFiberResult)
            {
                switch (item)
                {
                    case SingleCalculatedFiberItem single:
                        var idx = itemIndex;
                        // 单组分模板里 `Qualitative` 是**不索引**的（只有一个），没有 Qualitative_1；
                        // 写索引名会被 ReplaceText 静默丢弃 —— 定性栏就是这样恒空的。
                        flatData["Qualitative"] = single.Qualitative;
                        flatData[$"Reagent_{idx}"] = single.Reagent;
                        flatData[$"Sample_{idx}"] = "-";
                        flatData[$"Rate_{idx}"] = single.Rate.ToString("F2")+"%";
                        itemIndex++;
                        break;

                    case MultiCalculatedFiberItem multi:
                        flatData["Qualitative"] = multi.Qualitative;
                        flatData["Reagent"] = multi.Reagent;
                        flatData["Sample"] = multi.Sample;  // 页眉 Sample 书签
                        flatData["GSMTrail1"] = multi.GSMTrail1.ToString("F4");
                        flatData["GSMTrail2"] = multi.GSMTrail2.ToString("F4");
                        flatData["RateTrail1"] = multi.RateTrail1.ToString("F2")+"%";
                        flatData["RateTrail2"] = multi.RateTrail2.ToString("F2")+"%";
                        flatData["Rate"] = multi.Rate.ToString("F2")+"%";
                        flatData["Avg"] = multi.Avg.ToString("F2") + "%";

                        // 展开 MultiFiberRowUnits
                        if (multi.MultiFiberRowUnits != null)
                        {
                            var rowData = ExpandMultiFiberRowUnits(multi.MultiFiberRowUnits);
                            foreach (var kv in rowData)
                            {
                                flatData[kv.Key] = kv.Value;
                            }

                            // 平行样差异 >1% 标红
                            int rowIdx = 1;
                            foreach (var unit in multi.MultiFiberRowUnits)
                            {
                                if (unit.RateTrail1 > 0 && unit.RateTrail2 > 0
                                    && Math.Abs(unit.RateTrail1 - unit.RateTrail2) > 1m)
                                {
                                    redBookmarks.Add($"RateTrail1_{rowIdx}");
                                    redBookmarks.Add($"RateTrail2_{rowIdx}");
                                }
                                rowIdx++;
                            }

                            // Bottle / Crucible 编号与 Weighing Bottle 表**只认真实成分行**：
                            // 父槽下插的子行没有称量值（GSMTrail 全 0），进去会印出「瓶重 = 总重」的假数据，
                            // 坩埚数也会多算几支。结果表那一份（上面 ExpandMultiFiberRowUnits）照旧带它们。
                            var bottleUnits = multi.MultiFiberRowUnits
                                .Where(u => !u.IsDisplaySubRow)
                                .ToList();

                            // Bottle = 唯一 Yarn 名
                            var yarnNames = bottleUnits
                                .Select(u => u.Section)
                                .Where(s => !string.IsNullOrWhiteSpace(s) && s != "/")
                                .Distinct()
                                .ToList();

                            // Crucible = Section 变化时开新组，每组组分数-1
                            var crucibleCounts = new List<int>();
                            int groupCount = 0;
                            string lastSection = "";
                            foreach (var unit in bottleUnits)
                            {
                                var section = unit.Section ?? "";
                                if (!string.IsNullOrWhiteSpace(section) && section != "/" && section != lastSection)
                                {
                                    if (groupCount > 1)
                                        crucibleCounts.Add(groupCount - 1);
                                    groupCount = 0;
                                    lastSection = section;
                                }
                                if (!string.IsNullOrWhiteSpace(unit.Sum) && !unit.Sum.Contains('/'))
                                    groupCount++;
                            }
                            if (groupCount > 1)
                                crucibleCounts.Add(groupCount - 1);

                            int totalNeeded = yarnNames.Count + crucibleCounts.Sum();
                            if (totalNeeded > 0)
                            {
                                // 种子由**报告号**派生，不是无种子 Random。
                                // 无种子的话：① 同一份合并稿里两段报告各跑一次 Adapt，
                                // 同一批物理瓶子会印出两组不同的编号（同一份文件里自相矛盾）；
                                // ② 同一条记录重新生成一次，瓶子号也会变，重印件与归档件对不上。
                                var rng = new Random(seed);
                                var numbers = Enumerable.Range(1, 99)
                                    .OrderBy(_ => rng.Next())
                                    .Take(totalNeeded)
                                    .ToList();
                                var bottleTexts = numbers.Take(yarnNames.Count)
                                    .Select(n => $"Bottle: {n}");
                                var crucibleTexts = numbers.Skip(yarnNames.Count)
                                    .Select(n => $"Crucible: {n}");
                                flatData["Bottle"] = string.Join("    ",
                                    bottleTexts.Concat(crucibleTexts));
                            }

                            // Weighing Bottle 表 —— 行号按**过滤后**的行序编（它有自己那套 Description/Component 书签）
                            var weighingData = ExpandWeighingBottleData(bottleUnits, seed);
                            foreach (var kv in weighingData)
                                flatData[kv.Key] = kv.Value;
                        }
                        break;
                }
            }

            return flatData;
        }


        /// <summary>
        /// 展开 MultiFiberRowUnits 为带索引的扁平字典
        /// 
        /// 规则：
        /// 1. 相同的 Section（Yarn #x）只出现一次，后续同组行 Section 为空
        /// 2. 下标按实际行数连续编号
        /// 3. 空 Section 不占位，但其他字段正常编号
        /// 
        /// 示例：
        /// 输入: [Yarn#1, Yarn#2, Yarn#3, Yarn#4, Yarn#4, Yarn#4, Yarn#5, Yarn#5]
        /// 输出: Section_1=Yarn#1, Section_2=Yarn#2, Section_3=Yarn#3, Section_4=Yarn#4, 
        ///       Section_5=, Section_6=, Section_7=Yarn#5, Section_8=
        ///       Sum_1=..., Sum_2=..., ... Sum_8=...
        /// </summary>
        private Dictionary<string, string> ExpandMultiFiberRowUnits(List<MultiFiberRowUnit> units)
        {
            var result = new Dictionary<string, string>();
            int rowIndex = 1;

            // 记录上一个 Section，用于判断是否需要显示
            string lastSection = string.Empty;

            foreach (var unit in units)
            {
                // Section 处理：与上一个不同则显示，相同则为空
                string sectionValue;
                if (unit.Section != lastSection)
                {
                    sectionValue = unit.Section;
                    lastSection = unit.Section;
                }
                else
                {
                    sectionValue = string.Empty;
                }

                // 写入当前行的所有字段
                result[$"Section_{rowIndex}"] = sectionValue;
                // 父槽下插的展示子行（纤维素 / 复合纤维）**缩进两格**，一眼看出它是上一行拆出来的。
                // 缩进只发生在展示层 —— 领域层存的仍是光名字（页脚汇总、结果表都按原名认）。
                result[$"Sum_{rowIndex}"] = unit.IsDisplaySubRow ? "  " + unit.Sum : unit.Sum;
                result[$"GSMTrail1_{rowIndex}"] = unit.GSMTrail1 == 0 ? "" : unit.GSMTrail1.ToString("F4");
                result[$"GSMTrail2_{rowIndex}"] = unit.GSMTrail2 == 0 ? "" : unit.GSMTrail2.ToString("F4");
                result[$"RateTrail1_{rowIndex}"] = unit.RateTrail1 == 0 ? "" : unit.RateTrail1.ToString("F2") + "%";
                result[$"RateTrail2_{rowIndex}"] = unit.RateTrail2 == 0 ? "" : unit.RateTrail2.ToString("F2") + "%";
                result[$"Avg_{rowIndex}"] = unit.Avg == 0 ? "" : unit.Avg.ToString("F2") + "%";
                result[$"Correct_{rowIndex}"] = unit.Correct == 0 ? "" : unit.Correct.ToString("F2");
                // 判据是"表里有没有这个数"，**不是**"是不是 0" —— 真值 0（如 Polyurethane 的
                // ISO 回潮率）要印 0.00%，查不到的（组头行缩写串、表里没有的纤维名）才留空。
                result[$"MoistureRegain_{rowIndex}"] = unit.MoistureRegainKnown
                    ? unit.MoistureRegain.ToString("F2") + "%"
                    : "";
                result[$"Rate_{rowIndex}"] = unit.Rate == 0 ? "" : unit.Rate.ToString("F2") + "%";

                rowIndex++;
            }

            return result;
        }

        /// <summary>
        /// 把结果行拆成「百分比列 + 名称列」。
        /// 多组分：`65.0% Cotton` → (`65.0%`, `Cotton`)；单组分：`a:  100% Modal` → (`a:  100%`, `Modal`)。
        /// </summary>
        /// <remarks>
        /// 只认**第一个** %：双组分父行 `40.0% Bicomponent Fiber (60%Polyester 40%Polyamide)`
        /// 括号里的 % 不会被误切。行首空白先 Trim 掉，名称列因此不会带着前导空格
        /// （域层已不补对齐空格，这个 Trim 现在防的是历史文件与外部输入）。
        /// 没有 % 的行整行进名称列、百分比留空。
        /// </remarks>
        internal static (string Percent, string Name) SplitFiberResultLine(string line)
        {
            string text = (line ?? string.Empty).Trim();
            int pct = text.IndexOf('%');
            if (pct < 0)
                return (string.Empty, text);

            return (text[..(pct + 1)].TrimEnd(), text[(pct + 1)..].Trim());
        }

        /// <summary>
        /// 页脚回潮率的取位：**第二位小数为 0 就只留一位**（8.50 → <c>8.5</c>、4.00 → <c>4.0</c>），
        /// 有值才印两位（5.75 → <c>5.75</c>）。
        /// </summary>
        /// <param name="keepTwoDecimals">
        /// 恒两位（`8.50`）—— 欧洲（ISO1833 / DIN / EN / BS）与澳洲（AS 2001.7）体系走这条，
        /// 由 <paramref name="moistureRegain"/> 所属记录的回潮率列决定，见
        /// <see cref="MoistureRegainResolver.UsesIsoSchedule"/>。
        /// </param>
        /// <remarks>
        /// 只用于页脚那串汇总，**不动** MR 栏那一列（栏里恒两位，含真值 0 的 <c>0.00</c>）。
        /// 判法是先取到「分」位再问它是不是 0，不用字符串去尾零 —— 那样会连 <c>4.00</c> 的整数部分一起看错。
        /// </remarks>
        private static string FormatFooterMoistureRegain(decimal moistureRegain, bool keepTwoDecimals)
        {
            var twoDecimals = decimal.Round(moistureRegain, 2);
            if (keepTwoDecimals) return twoDecimals.ToString("F2");

            return twoDecimals == decimal.Round(twoDecimals, 1)
                ? twoDecimals.ToString("F1")
                : twoDecimals.ToString("F2");
        }

        /// <summary>
        /// 由报告号派生一个**跨进程稳定**的随机种子。
        /// </summary>
        /// <remarks>
        /// 不能用 string.GetHashCode()：.NET Core 起它对每个进程随机加盐，
        /// 同一个报告号在服务重启后会得到完全不同的瓶号 —— 那"重印件与归档件对得上"这条就没修掉。
        /// 这里用 FNV-1a（UTF-16 按两个字节展开），纯算术、与运行时无关。
        /// </remarks>
        private static int StableSeed(string? text)
        {
            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;

                uint hash = offsetBasis;
                foreach (char c in text ?? string.Empty)
                {
                    // 一个 char 拆两个字节喂进去 —— 光用 code unit 的高位在小字符集上几乎不动，散不开
                    uint code = c;
                    hash = (hash ^ (code & 0xFFu)) * prime;
                    hash = (hash ^ (code >> 8)) * prime;
                }

                return (int)hash;
            }
        }

        /// <summary>
        /// 展开 Weighing Bottle 表数据。
        /// 规则：一个溶解组（相同 Section）共用一个称量瓶，A/B 两平行试验各自一组值。
        /// WeighingA/WeighingB 只在 Section 首行填值，TotalA/TotalB 每行都填。
        /// </summary>
        /// <param name="seed">
        /// 由报告号派生的种子。原先是无种子 Random，于是同一份合并稿里
        /// 两段报告会给**同一个物理称量瓶**印出两组不同的 A/B 空瓶重 —— 与瓶号/坩埚号是同一个毛病。
        /// </param>
        private static Dictionary<string, string> ExpandWeighingBottleData(List<MultiFiberRowUnit> units, int seed)
        {
            var result = new Dictionary<string, string>();
            var rng = new Random(seed);
            string lastSection = "";
            decimal weighingA = 0m;
            decimal weighingB = 0m;
            string lastDescription = "";

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                var row = i + 1;

                // Section 变 → 新称量瓶
                if (unit.Section != lastSection || string.IsNullOrWhiteSpace(unit.Section))
                {
                    if (!string.IsNullOrWhiteSpace(unit.Section) && unit.Section != "/")
                    {
                        weighingA = (260000m + rng.Next(0, 90001)) / 10000m;
                        weighingB = (260000m + rng.Next(0, 90001)) / 10000m;
                        lastSection = unit.Section;
                        result[$"WeighingA{row}"] = weighingA.ToString("F4");
                        result[$"WeighingB{row}"] = weighingB.ToString("F4");
                    }
                }
                else
                {
                    // 同 Section 后续行留空
                    result[$"WeighingA{row}"] = "";
                    result[$"WeighingB{row}"] = "";
                }

                // Description — Section 去重逻辑（同 ExpandMultiFiberRowUnits）
                if (!string.IsNullOrWhiteSpace(unit.Section) && unit.Section != lastDescription)
                {
                    result[$"Description{row}"] = unit.Section;
                    lastDescription = unit.Section;
                }
                else
                {
                    result[$"Description{row}"] = "";
                }

                // Component
                result[$"Component{row}"] = unit.Sum;

                // sampleA / sampleB — GSMTrail 为 0 则留空
                result[$"sampleA{row}"] = unit.GSMTrail1 == 0 ? "" : unit.GSMTrail1.ToString("F4");
                result[$"sampleB{row}"] = unit.GSMTrail2 == 0 ? "" : unit.GSMTrail2.ToString("F4");

                // TotalA / TotalB — 每行都填（用组瓶重 + 当前行 GSMTrail）
                result[$"TotalA{row}"] = (weighingA + unit.GSMTrail1).ToString("F4");
                result[$"TotalB{row}"] = (weighingB + unit.GSMTrail2).ToString("F4");
            }

            return result;
        }
    }
}
