using System;
using System.Collections.Generic;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis
{
    /// <summary>
    /// 国标报告的**中文化** —— 闸门、纤维名的拼法、五个标签的替换表，三件事都在这一处。
    ///
    /// **闸门只认一个值**：<c>FZ/T 01057</c>。method 下拉里没有 "GB/T …" 这个选项
    /// （见 <c>FiberWorksheetService.MethodOptionsCurrent</c>），国产项就这一个系列名，
    /// 它由 <see cref="FiberStandardChainBuilder"/> 展开出 <c>.1~.4</c> 与 <c>GB/T 2910.x</c>。
    /// 刻意**不**按"链里含 GB/T"判 —— 链里同时混着 FZ/T 与 ISO/TR 11827，字符串判会误伤。
    /// 也**不**认 <c>FZ/T 01057.1-4–2007</c> 这类库里存的旧串：那是老记录，不是勾选值。
    ///
    /// 与 AATCC 那支天然互斥（<c>IngredientAnalysisCalculation.GenerateRecommendedLabel</c>
    /// 里的 isAatcc 同样读本段标准），两支不会同时成立。
    /// </summary>
    internal static class FiberChineseName
    {
        /// <summary>触发中文化的那个 method 值。</summary>
        public const string GbStandard = "FZ/T 01057";

        /// <summary>
        /// 报告模板里那五个**纯文本**标签 → 中文。
        ///
        /// 它们在 .docx 里是光秃秃的文字、连书签都没有，所以换不了"值"，
        /// 只能让引擎按整段文字匹配后改写（见 <c>WordTemplateEngine.ReplaceText</c> 的
        /// <c>replaceParagraphText</c> 参数）。
        ///
        /// 冒号用**半角** —— 与模板里英文那行同宽，且与工作簿里 `单号：`/`测试方法：` 的
        /// 全角风格不一致是刻意的（这批标签对齐的是英文原文的版式）。
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ReportLabels = new Dictionary<string, string>
        {
            ["Test Result:"] = "测试结果:",
            ["Based on moisture regain weight:"] = "结合公定回潮率的含量:",
            ["Recommendation:"] = "推荐标签:",
            ["Conclusion:"] = "结论:",
            ["Remark:"] = "备注:",
        };

        /// <summary>本段这一份是否出中文。</summary>
        public static bool IsChineseReport(string? standard)
            => standard?.Trim().Equals(GbStandard, StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// 纤维名 → <c>英文原名 + 中文名</c>（如 <c>Cotton棉</c>）；查不到就原样返回英文。
        ///
        /// 英文在前、中文紧跟、中间不加空格。
        /// 查表时 Trim、返回时保留原样：名字是别的通道来的，不该在这里被悄悄规整。
        /// </summary>
        public static string Localize(string? name, IReadOnlyDictionary<string, string>? map)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
            if (map == null || map.Count == 0) return name;

            return map.TryGetValue(name.Trim(), out var cn) && !string.IsNullOrWhiteSpace(cn)
                ? name + cn
                : name;
        }

        /// <summary>
        /// 复合纤维父行的中文名 —— **固定** <c>聚酯/聚酰胺复合纤维</c>。
        /// </summary>
        public const string CompositeFiberName = "聚酯/聚酰胺复合纤维";

        /// <summary>
        /// 本份含复合纤维时，其余 Polyamide/Nylon 在正文里的中文名（页脚印 <c>聚酰胺纤维</c>）。
        /// </summary>
        public const string CompositePolyamideName = "聚酰胺";

        /// <summary>
        /// 括号内子纤维的中文名走**报告口径**（聚酯 / 聚酰胺），与父行同一个叫法；
        /// 不取库里的 `聚酯纤维` / `锦纶`。表里没有的回落库中文名，库里也没有就留英文。
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> CompositeComponentNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Polyester"] = "聚酯",
                ["Polyamide"] = "聚酰胺",
                ["Nylon"] = "聚酰胺",
            };

        /// <summary>
        /// 复合纤维括号内**单个子纤维**的中文名：<c>Polyester聚酯</c>，与 <see cref="Localize"/> 同一拼法。
        /// </summary>
        public static string LocalizeCompositeComponent(string? name, IReadOnlyDictionary<string, string>? dbNames)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;

            if (CompositeComponentNames.TryGetValue(name.Trim(), out var reportName))
                return name + reportName;

            return dbNames != null && dbNames.TryGetValue(name.Trim(), out var dbName)
                   && !string.IsNullOrWhiteSpace(dbName)
                ? name + dbName
                : name;
        }

        /// <summary>
        /// 页脚 MR 汇总的中文尾巴：本段含复合纤维时，`锦纶` 改印 `聚酰胺纤维`。
        ///
        /// 只换**以锦纶结尾**的名字（`Polyamide锦纶` / `Nylon锦纶`），英文部分与其余纤维一概不动。
        /// 只喂页脚：结果行、称量表、定性串都不经过这里。
        /// </summary>
        public static string CompositeFooterName(string localized)
        {
            const string polyamideCn = "锦纶";
            const string compositePolyamideCn = "聚酰胺纤维";

            if (string.IsNullOrEmpty(localized)) return localized ?? string.Empty;

            return localized.EndsWith(polyamideCn, StringComparison.Ordinal)
                ? localized[..^polyamideCn.Length] + compositePolyamideCn
                : localized;
        }
    }
}
