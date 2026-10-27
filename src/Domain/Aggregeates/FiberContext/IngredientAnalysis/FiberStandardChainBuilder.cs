using System;
using System.Collections.Generic;
using System.Linq;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.Enums;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis
{
    /// <summary>
    /// 方法标准链规则表 —— 由 <see cref="IngredientAnalysisCalculation"/> 搬出，规则在此维护。
    ///
    /// 这一层改的都是**规则与常量**，不改结构：
    ///   · ISO1833_20 年份 2020 → **2018**（2020 只是各国采标年份，不存在这个标准号）
    ///   · DIN 分部改用 **DIN 自己的版本表**（<see cref="DinPartOf"/>，以 `成分标准终.xlsx`
    ///     的 DIN 表为准）：-3/-6/-12/-18/-20/-22 六个分部与 ISO 表年份不同 ——
    ///     原先拿 ISO 常量拼 "DIN EN " 前缀，印不出 DIN 的年份
    ///   · GB/T 2910.7 2009 → **2025**（当前版本；2C 口径下仍是 2009）
    ///   · 显微镜法串 FZ/T 30003-2009 → **2024**；2026-09 又去掉其中的 "FZ/T 01057.3–2007 / "
    ///     前缀 —— .3 已改由 FZ/T 01057 系列名展开落到链首（见 FZ01057_* 常量），留着就重复了。
    ///     四个分部一律 **2007 版**：.2/.3 的 2025 版 2027-05-01 才实施，届时才换。
    ///   · ISO 侧：-1（试验通则，不是纤维对的定量方法）改 -20；-22 上移到 -6 之前；
    ///     -6 按官方范围收紧；补 flax（不补 ramie，1833-22:2020 只有 flax）
    ///   · GB 侧：删除"聚酯在前一律给 2910.24"的兜底，换成涤氨双向 2910.20；补纤维素 × 弹性纤维 2910.20
    ///   · 丙烯腈三处裸字面量（ISO -12、GB .12、设备选型）统一走 <see cref="FiberTokens.IsAcrylicType"/>
    ///   · -12 / .12 三条兜底的后位判据由"在 1833-12 范围内"（对象 ∪ 其他，并集）收紧成
    ///     <see cref="FiberTokens.IsIso1833_12OtherFibre"/>（**只有"其他纤维"**）：两栏本就互斥，
    ///     对象纤维（腈纶 / 弹性纤维）不能互相配对，DMF 会把两样一起溶掉。
    ///
    /// **丙烯腈守卫**：ISO 1833-20 / GB/T 2910.20 官方都声明"不适用于聚丙烯腈纤维同时存在的情况"，
    /// 所以这两条规则必须看到**整条纤维列表**，不能只看当前这一对——故两个查表函数都收 hasAcrylic。
    /// 守卫生效时退回 -12（1833-20 官方注列出的备选方法），而不是落空：否则
    /// (Elastane, 纤维素) 在含丙烯腈时会落空、(纤维素, Elastane) 却不会，又变回
    /// "同一对因录入顺序给不同结果"——凡成对的判定，两个方向必须一致。
    ///
    /// 守卫回退**不会发出范围外的 -12**：三条 .20 规则的合作方只可能是纤维素类、聚酯、聚酰胺，
    /// 三者全部落在 <see cref="FiberTokens.IsIso1833_12OtherFibre"/> 里，故退回去的 -12 后位仍够格。
    ///
    /// <para>
    /// <b>规则与渲染分开</b>：查表只产出**分部号**（<see cref="ResolveIsoPart"/> /
    /// <see cref="ResolveGbPart"/>，<c>int?</c>，<c>null</c> = 落空），串由族渲染表
    /// （<see cref="IsoPartOf"/> / <see cref="GbPartOf"/> / <see cref="RenderIsoSub"/>）给。
    /// 对外的 <see cref="LookupSubStandard"/> / <see cref="LookupGbSubStandard"/> 保持原签名与返回值，
    /// 是这套拆分的薄包装 —— 所以 <c>golden/pair-coverage.csv</c> 逐字不变。
    /// </para>
    ///
    /// <para>
    /// <b>两个维度：族 × 模式</b>。族（<see cref="StandardFamily"/>）决定前缀与各自的版本表；
    /// 模式（<see cref="StandardMode"/>）决定取**当前版本**还是**双资质版本**。
    /// 渲染因此是四族 × 两模式的选表，规则体一处都不分叉。
    /// 2C 表只列与常规**不同**的分部、其余转回常规表，所以"2C 改了什么"在代码里是看得见的差分；
    /// 落空前缀 <c>string.Empty</c>，调用方负责跳过（否则会挤出多余空格）。
    /// </para>
    /// </summary>
    internal static class FiberStandardChainBuilder
    {
        private const string ISO_QUALITATIVE = "ISO/TR 11827:2012";
        private const string DIN_QUALITATIVE = "DIN CEN ISO/TR 11827:2019";
        private const string ISO1833_1 = "ISO1833-1:2020";
        private const string ISO1833_2 = "ISO1833-2:2020";
        private const string ISO1833_3 = "ISO1833-3:2020";
        private const string ISO1833_4 = "ISO1833-4:2023";
        private const string ISO1833_6 = "ISO1833-6:2018";
        private const string ISO1833_7 = "ISO1833-7:2017";
        private const string ISO1833_11 = "ISO1833-11:2017";
        private const string ISO1833_12 = "ISO1833-12:2020";
        private const string ISO1833_18 = "ISO1833-18:2020";
        private const string ISO1833_20 = "ISO1833-20:2018";
        private const string ISO1833_22 = "ISO1833-22:2020";
        // GB/T 2910.x 子标准常量。分隔符是 – (U+2013)，与原表一致。
        private const string GB2910_1 = "GB/T 2910.1–2009";
        private const string GB2910_2 = "GB/T 2910.2–2009";
        private const string GB2910_3 = "GB/T 2910.3–2009";
        private const string GB2910_4 = "GB/T 2910.4–2022";
        private const string GB2910_6 = "GB/T 2910.6–2009";
        private const string GB2910_7 = "GB/T 2910.7–2025";
        private const string GB2910_11 = "GB/T 2910.11–2024";
        private const string GB2910_12 = "GB/T 2910.12–2023";
        private const string GB2910_18 = "GB/T 2910.18–2009";
        private const string GB2910_20 = "GB/T 2910.20–2009";
        private const string GB2910_22 = "GB/T 2910.22–2009";
        private const string FZ01026 = "FZ/T 01026–2017";

        // ── FZ/T 01057 系列（纺织纤维鉴别试验方法）────────────────────────────
        // 下拉里**只有系列名一条**（见 FiberWorksheetService.MethodOptionsCurrent），报告上落的却是
        // .1~.4 四个分部 —— 由下面 BuildSingleStdChain 的 GB 分支展开，系列名本身不上报告。
        // 与 ISO1833 / DIN EN ISO 1833 只写体系名、分部由链构造器补全是同一个约定。
        //
        // 写法逐字照抄官方标准号：年份前一律 ASCII 连字符 —— 与相邻 FZ01026 的 –(U+2013) 不同，
        // 是两套清单的原始写法不同，别顺手统一。
        private const string FZ01057 = "FZ/T 01057";
        private const string FZ01057_1 = "FZ/T 01057.1-2007";  // 通用说明
        private const string FZ01057_2 = "FZ/T 01057.2-2025";  // 燃烧法（2027-05-01 才实施，提前采用）
        private const string FZ01057_3 = "FZ/T 01057.3-2025";  // 显微镜法（2027-05-01 才实施，提前采用）
        private const string FZ01057_4 = "FZ/T 01057.4-2007";  // 溶解法

        /// <summary>
        /// 纤维素类父槽存在时追加的显微镜法串。
        /// </summary>
        private const string MICROSCOPE_GB_CHAIN = "FZ/T 30003-2024";

        /// <summary>
        /// 标准**族** —— 同一个分部号在不同族下渲染成不同的串。
        ///
        /// 四族共用 <see cref="ResolveIsoPart"/> 那一份组分对规则，差别只在渲染：
        /// <see cref="IsoPartOf"/> / <see cref="DinPartOf"/> / <see cref="EnPartOf"/> /
        /// <see cref="BsPartOf"/> 是**四张独立的版本表**（各自的采标版次自成一套，年份不能互相借）。
        ///
        /// EN / BS 两族与 ISO **同构**：领队串、分部号、三元短路全都一样，
        /// 差别只有前缀与年份。唯一的族特有处是 **xlsx 里 EN / BS 两表没有 `ISO 20705` 那一行**，
        /// 故不做 cellulosic 那串（见 <see cref="BuildSingleStdChain"/>）。
        /// </summary>
        private enum StandardFamily
        {
            Iso,
            Din,
            En,
            Bs,
        }

        /// <summary>
        /// 分部号 → ISO 1833 标准串（**常规口径**）。
        ///
        /// **查表只产出编号**（见 <see cref="ResolveIsoPart"/>），串由这里给 ——
        /// "哪一对该出哪个分部"与"这个分部怎么写"因此分成两件事，
        /// 换年份/换写法不必碰规则体，族前缀也不必再靠字符串集合做替换。
        /// </summary>
        private static string IsoPartOf(int part) => part switch
        {
            1 => ISO1833_1,
            2 => ISO1833_2,
            3 => ISO1833_3,
            4 => ISO1833_4,
            6 => ISO1833_6,
            7 => ISO1833_7,
            11 => ISO1833_11,
            12 => ISO1833_12,
            18 => ISO1833_18,
            20 => ISO1833_20,
            22 => ISO1833_22,
            _ => string.Empty,
        };

        /// <summary>
        /// 分部号 → **DIN 采标版**标准串（常规口径）。
        ///
        /// DIN 是自己一套版次，年份**不能从 ISO 常量借**：`-3/-6/-12/-18/-20/-22`
        /// 六个分部与 <see cref="IsoPartOf"/> 不同（以 `成分标准终.xlsx` 的 DIN 表 A 列为准）。
        /// 写法统一 `DIN EN ISO 1833-x:年`（**带空格**），不再靠拼前缀。
        /// </summary>
        private static string DinPartOf(int part) => part switch
        {
            1 => "DIN EN ISO 1833-1:2020",
            2 => "DIN EN ISO 1833-2:2020",
            3 => "DIN EN ISO 1833-3:2021",
            4 => "DIN EN ISO 1833-4:2023",
            6 => "DIN EN ISO 1833-6:2019",
            7 => "DIN EN ISO 1833-7:2017",
            11 => "DIN EN ISO 1833-11:2017",
            12 => "DIN EN ISO 1833-12:2021",
            18 => "DIN EN ISO 1833-18:2021",
            20 => "DIN EN ISO 1833-20:2019",
            22 => "DIN EN ISO 1833-22:2021",
            _ => string.Empty,
        };

        /// <summary>分部号 → **EN 采标版**标准串（常规口径），写法 `EN ISO 1833-x:年`。</summary>
        private static string EnPartOf(int part) => part switch
        {
            1 => "EN ISO 1833-1:2020",
            2 => "EN ISO 1833-2:2020",
            3 => "EN ISO 1833-3:2020",
            4 => "EN ISO 1833-4:2023",
            6 => "EN ISO 1833-6:2019",
            7 => "EN ISO 1833-7:2017",
            11 => "EN ISO 1833-11:2017",
            12 => "EN ISO 1833-12:2020",
            16 => "EN ISO 1833-16:2019",
            18 => "EN ISO 1833-18:2020",
            20 => "EN ISO 1833-20:2019",
            22 => "EN ISO 1833-22:2021",
            _ => string.Empty,
        };

        /// <summary>分部号 → **BS 采标版**标准串（常规口径），写法 `BS EN ISO 1833-x:年`。</summary>
        private static string BsPartOf(int part) => part switch
        {
            1 => "BS EN ISO 1833-1:2020",
            2 => "BS EN ISO 1833-2:2020",
            3 => "BS EN ISO 1833-3:2020",
            4 => "BS EN ISO 1833-4:2023",
            6 => "BS EN ISO 1833-6:2019",
            7 => "BS EN ISO 1833-7:2017",
            11 => "BS EN ISO 1833-11:2017",
            12 => "BS EN ISO 1833-12:2020",
            16 => "BS EN ISO 1833-16:2019",
            18 => "BS EN ISO 1833-18:2020",
            20 => "BS EN ISO 1833-20:2019",
            22 => "BS EN ISO 1833-22:2021",
            _ => string.Empty,
        };

        /// <summary>分部号 → GB/T 2910 标准串（常规口径，分工同 <see cref="IsoPartOf"/>）。</summary>
        private static string GbPartOf(int part) => part switch
        {
            1 => GB2910_1,
            2 => GB2910_2,
            3 => GB2910_3,
            4 => GB2910_4,
            6 => GB2910_6,
            7 => GB2910_7,
            11 => GB2910_11,
            12 => GB2910_12,
            18 => GB2910_18,
            20 => GB2910_20,
            22 => GB2910_22,
            _ => string.Empty,
        };

        // ── 2C 表：只列与常规**不同**的分部，其余转回常规表 ──────────────────────
        // 依据 `成分标准终.xlsx`（2026-10-26 版）各表的 CNAS / CMA 列：两列打架取旧的，写着 `/` 的一律落空。

        /// <summary>`-4` 的 CNAS / CMA 都是 2023，与常规同值；只有 `-22` 两列都写 `/`。</summary>
        private static string IsoPartOfDual(int part) => part switch
        {
            22 => string.Empty,
            _ => IsoPartOf(part),
        };

        /// <summary>DIN 表的 CNAS 列**逐格等于常规列**，只有 `-22` 写 `/` —— 所以只有它落空。</summary>
        private static string DinPartOfDual(int part) => part switch
        {
            22 => string.Empty,
            _ => DinPartOf(part),
        };

        /// <summary>`-4`（2023 → 2020）、`-12`（2020 → 2017）落后于当前版本；`-16` / `-22` 写 `/`。</summary>
        private static string EnPartOfDual(int part) => part switch
        {
            4 => "EN ISO 1833-4:2020",
            12 => "EN ISO 1833-12:2017",
            16 => string.Empty,
            22 => string.Empty,
            _ => EnPartOf(part),
        };

        /// <summary>BS 表的 CNAS 与常规版本同值，只有 `-16` / `-22` 写 `/`。</summary>
        private static string BsPartOfDual(int part) => part switch
        {
            16 => string.Empty,
            22 => string.Empty,
            _ => BsPartOf(part),
        };

        /// <summary>
        /// `-4` / `-11` / `-12` 的 CNAS / CMA 已与常规同值（2022 / 2024 / 2023），不再回退；
        /// `-7` 的 CNAS / CMA **都是 2009**；`-22` 两列都写 `/`。分隔符仍是 `–`(U+2013)，只换年份。
        /// </summary>
        private static string GbPartOfDual(int part) => part switch
        {
            7 => "GB/T 2910.7–2009",
            22 => string.Empty,
            _ => GbPartOf(part),
        };

        /// <summary>
        /// 把分部号渲染成**某一族 × 某一模式**的串 —— 只是选表，规则体不在这里。
        /// 返回空串 = 该族该模式下这个分部落空，**调用方负责跳过**（别让空串占位）。
        /// </summary>
        private static string RenderIsoSub(StandardFamily family, StandardMode mode, int part)
        {
            var dual = mode == StandardMode.Dual;
            return family switch
            {
                StandardFamily.Iso => dual ? IsoPartOfDual(part) : IsoPartOf(part),
                StandardFamily.Din => dual ? DinPartOfDual(part) : DinPartOf(part),
                StandardFamily.En => dual ? EnPartOfDual(part) : EnPartOf(part),
                _ => dual ? BsPartOfDual(part) : BsPartOf(part),
            };
        }

        /// <summary>分部号 → GB/T 2910 串，按模式选表（GB 没有"族"这一维，故单独一个选表口）。</summary>
        private static string GbPartOf(StandardMode mode, int part)
            => mode == StandardMode.Dual ? GbPartOfDual(part) : GbPartOf(part);

        /// <summary>
        /// FZ/T 01057 系列的某一个分部 —— **2C 下 .2/.3 退回 2007 版**（双资质只到 2007），
        /// .1/.4 两模式同值。
        /// </summary>
        private static string Fz01057Part(int part, StandardMode mode) => (part, mode) switch
        {
            (2, StandardMode.Dual) => "FZ/T 01057.2-2007",
            (3, StandardMode.Dual) => "FZ/T 01057.3-2007",
            (1, _) => FZ01057_1,
            (2, _) => FZ01057_2,
            (3, _) => FZ01057_3,
            (4, _) => FZ01057_4,
            _ => string.Empty,
        };

        /// <summary>FZ/T 01057 系列名在报告上展开的四个分部（.1~.4）。</summary>
        private static string[] Fz01057Series(StandardMode mode) => new[]
        {
            Fz01057Part(1, mode), Fz01057Part(2, mode), Fz01057Part(3, mode), Fz01057Part(4, mode),
        };

        /// <summary>
        /// 链首的**鉴别法**串。ISO / EN / BS 三族一律 <see cref="ISO_QUALITATIVE"/>
        /// （xlsx 的 EN / BS 两表没有这一行，照 ISO 模式领队）；DIN 用自己的采标版。
        ///
        /// <para>
        /// <b>刻意不分模式</b>：xlsx 的 DIN 表在鉴别法那一格写 `/`，但 2C 下**照样印**
        /// —— 鉴别法是定性报告的立足点，链里不能没有它；而且它是**族**的属性，
        /// 与"哪个版本的定量分部有双资质"不是一回事。故这里不收 <see cref="StandardMode"/>参数，是设计而不是漏传。
        /// </para>
        /// </summary>
        private static string QualitativeFor(StandardFamily family)
            => family == StandardFamily.Din ? DIN_QUALITATIVE : ISO_QUALITATIVE;

        /// <summary>
        /// ISO 侧纤维素父槽的显微镜串。2C 下 `ISO 20705` 只有 CNAS 有资质、CMA 写 `/` → 落空。
        /// </summary>
        private static string IsoMicroscopeChain(StandardMode mode)
            => mode == StandardMode.Dual ? string.Empty : "ISO 20705:2019";

        /// <summary>GB 侧纤维素父槽的显微镜串 —— 2C 下 CNAS / CMA 都只到 **2009**。</summary>
        private static string MicroscopeGbChain(StandardMode mode)
            => mode == StandardMode.Dual ? "FZ/T 30003-2009" : MICROSCOPE_GB_CHAIN;

        /// <summary>空串不占位 —— 2C 下会有分部落空，不能让它们挤出多余空格。</summary>
        private static void AddIfNotEmpty(List<string> parts, string value)
        {
            if (!string.IsNullOrEmpty(value)) parts.Add(value);
        }

        /// <summary>
        /// Excel L4+L6: 根据标准体系和成分对自动拼接方法标准链。
        ///
        /// **两参数重载 = 全部槽都是叶子槽**（无子纤维）。无子纤维的记录全走这条，
        /// 输出与引入槽位通道之前**逐字一致**——这本身就是槽位通道的行为契约：
        /// "没有子纤维时，槽位通道不产生任何新配对"。
        /// 需要子纤维参与配对（亚麻那一类）才用三参数重载。
        /// </summary>
        internal static string BuildMethodString(string standard, List<string> fibers)
            => BuildMethodString(standard, fibers, fibers.Select(FiberSlot.Leaf).ToList());

        /// <summary>Excel L4+L6: 根据标准体系和成分对自动拼接方法标准链（含子纤维槽位）。</summary>
        /// <param name="isSingleComponent">
        /// 这条记录是不是**单组分**（聚合根的 <c>AnalysisType.Single</c>）。
        ///
        /// <para>
        /// <b>它是整条链的分流开关</b>：单组分走
        /// <see cref="BuildSingleComponentChain"/> —— **一律不派生定量子标准**，
        /// 只出勾选的标准本身 + 鉴别法（ISO/DIN 侧 <c>ISO/TR 11827</c>，
        /// GB 侧 <c>FZ/T 01057.3-2025</c>）。
        /// 默认 <c>false</c> = 多组分 = **改动前的行为**，所以既有调用点与全部测试
        /// 不传这个参数时输出逐字不变。
        /// </para>
        ///
        /// <para>
        /// <b>刻意是 bool 而不是 <c>AnalysisType</c></b>：本模块只看"是不是定性报告"这一个比特，
        /// 收枚举会让默认值无从选择（默认 <c>Single</c> 就把所有既有调用点的输出改了）。
        /// </para>
        ///
        /// <para>
        /// <b>纤维条数区分不出它</b>：单组分记录可以有 1 条也可以有多条单纤维.
        /// </para>
        /// </param>
        /// 那句 IsNullOrWhiteSpace 守卫要留着：
        /// <see cref="IngredientAnalysisCalculation.StandardsToRender"/> 在 Methods 为空时退化成
        /// **单个空标准**，走到这里就该返回空串，不是抛异常。
        /// </remarks>
        /// <param name="isSingleComponent">见上。</param>
        /// <param name="mode">
        /// 取值口径（<see cref="StandardMode.Current"/> = 常规 = 默认 = **改动前的行为**）。
        ///
        /// <para>
        /// <see cref="StandardMode.Dual"/>（2C）只影响**渲染**：年份换资质范围内的版本、
        /// 资质列写着 `/` 的分部落空。查表规则与链结构一处不分叉。
        /// 默认值保证既有调用点与全部测试**不传这个参数时输出逐字不变**。
        /// </para>
        ///
        /// <para>
        /// 这是**纯参数**，不落库 —— 模式只在生成报告这一趟内存里活着，2C 与否由文件名承载。
        /// </para>
        /// </param>
        internal static string BuildMethodString(
            string standard, List<string> fibers, IReadOnlyList<FiberSlot> slots,
            bool isSingleComponent = false, StandardMode mode = StandardMode.Current)
        {
            if (string.IsNullOrWhiteSpace(standard)) return string.Empty;

            return BuildSingleStdChain(standard, fibers, slots, isSingleComponent, mode);
        }

        /// <summary>
        /// 单个标准的链 —— 这是**唯一**路径，不再有"多值拆链"那种调用形态。
        ///
        /// 两个刻意的边界：
        ///   · **不按空格切分** —— AATCC TM20-2021  AATCC TM20A-2021e（双空格）
        ///     是 AATCC 的**复合方法名**（TM20 + TM20A 是一套），不是两个标准，必须整串透传。
        ///   · **顺序由 Methods 决定** —— 多标准的先后就是分析员的勾选顺序，不做规范化。
        /// </summary>
        private static string BuildSingleStdChain(
            string standard, List<string> fibers, IReadOnlyList<FiberSlot> slots,
            bool isSingleComponent, StandardMode mode)
        {
            if (string.IsNullOrWhiteSpace(standard)) return string.Empty;

            var isIso = standard.Equals("ISO1833", StringComparison.OrdinalIgnoreCase);
            var isDin = standard.Equals("DIN EN ISO 1833", StringComparison.OrdinalIgnoreCase);
            var isEn = standard.Equals("EN ISO 1833", StringComparison.OrdinalIgnoreCase);
            var isBs = standard.Equals("BS EN ISO 1833", StringComparison.OrdinalIgnoreCase);
            var isGb = standard.StartsWith("FZ/T", StringComparison.OrdinalIgnoreCase)
                    || standard.StartsWith("GB/T", StringComparison.OrdinalIgnoreCase);

            // 四族共用**同一份**组分对规则（ResolveIsoPart），差别只在渲染表 —— 见 StandardFamily。
            // 不是这四族（且不是 GB）时 family 为 null，后面一律当 GB 侧的 else 处理。
            StandardFamily? family = null;
            if (isIso) family = StandardFamily.Iso;
            else if (isDin) family = StandardFamily.Din;
            else if (isEn) family = StandardFamily.En;
            else if (isBs) family = StandardFamily.Bs;

            // 分析员勾的是不是 FZ/T 01057 系列（**鉴别**标准，自带显微法 .3）。
            // 用 StartsWith 而不是 Equals：真实库里既有系列名 `FZ/T 01057`，
            // 也有老记录的合并写法 `FZ/T 01057.1-4–2007`，两者都已含 .3。
            var hasIdentificationStd = standard.StartsWith(FZ01057, StringComparison.OrdinalIgnoreCase);

            // 非 ISO/DIN/EN/BS/GB：直接返回原值
            //（Regulation / AATCC / CAN / CNS / JIS / AS 一律逐字透传 —— 含 2C 模式）
            if (family is null && !isGb) return standard;

            // ★ 单组分**一律不派生**。
            // 单组分表里每行是**不同测点下的各一种组分**（每行一种纤维、各 100%），
            // 不是多组分那种"同一测点下的多种组分（一份混纺）"—— 按纤维列表查表的定量
            // 子标准（GB/T 2910.x、ISO 1833-x）前提是"一份混纺要拆"，对它不成立。
            // 分流点刻意放在上面那条透传守卫**之后**，Regulation / CAN / CNS / JIS
            // 两种类型都仍然是逐字透传。
            if (isSingleComponent) return BuildSingleComponentChain(standard, family, mode);

            // ── 以下全部是多组分路径，逐字未动 ──

            // 丙烯腈守卫要看到**整条列表**，不是当前这一对 —— 在此算一次，传给两个查表函数
            var hasAcrylic = fibers.Any(FiberTokens.IsAcrylicType);

            // 分组父槽字面量是否在列表里 —— 决定显微镜法那一串加不加。
            // 三条消费者（SelectEquipment 的显微镜追加、这里两处）都读**扁平列表**，
            // 所以槽位通道不改 GetOrderedFiberNames() 的返回内容（父槽字面量必须留在里面），
            // 这两条判定因此逐字未动。
            var hasCellulosicParent = fibers.Any(f => f == "*cellulosic fibre" || f == "*Regenerated cellulose fibre");

            // 三元法短路用的是**成分数**，父槽被其子纤维替换（1 换 N）、同名纤维只算一个，
            // 不是顶层槽数。无子纤维**且无重名**时它恒等于 slots.Count == fibers.Count，
            // 所以那 256 条输出逐字不变；重名的槽（同一根纤维录进两列）会因此少算一个，
            // 跨过 3 那道坎时输出会变 —— 见 EffectiveComponentCount 的注释。
            var count = EffectiveComponentCount(slots);

            // 这 3 个成分是不是**分组父槽展开**得来的（计数口径见 EffectiveComponentCount）。
            // ISO 侧决定 count == 3 时只给 -2 还是分部同给，GB 侧决定成分对分部列不列 —— 同一判据。
            var expanded = slots.Any(s => s.SubFibers.Count > 0);

            var parts = new List<string>();

            // ========== ISO / DIN / EN / BS（同构，只差渲染表）==========
            if (family is StandardFamily fam)
            {
                var qualitative = QualitativeFor(fam);
                var ternary = RenderIsoSub(fam, mode, 2);

                // EN / BS 两族在 xlsx 里**没有 ISO 20705 那一行**，故没有 cellulosic 那串。
                var microscope = fam == StandardFamily.En || fam == StandardFamily.Bs
                    ? string.Empty
                    : IsoMicroscopeChain(mode);

                AddIfNotEmpty(parts, qualitative);

                var subStandards = new List<string>();
                foreach (var (first, second) in EnumeratePairs(slots))
                {
                    if (ResolveIsoPart(first, second, hasAcrylic) is not int part) continue;

                    // 2C 下这个分部落空（资质列写 `/`）→ 直接跳过，不让它占位。
                    var s = RenderIsoSub(fam, mode, part);
                    if (s.Length == 0) continue;

                    if (!subStandards.Contains(s))
                        subStandards.Add(s);
                }

                // 三元法短路。**两种情形必须分开**：
                //
                //   · **三个独立成分**（没有槽展开）→ 只给 `-2`。这是规则表搬出以来的既有设计，
                //     `[Cotton, Modal, Elastane]` 逐字不变，是回归铁律，也是"无子纤维的记录
                //     输出逐字不变"这条槽位通道契约的落点。`subStandards` 在这里算出来但不输出。
                //
                //   · **3 是分组父槽展开得来的**（父槽 + N 子纤维 + 别的槽）→ 分部与 `-2` 同时给。
                //
                // GB 侧的三元短路（见下面 T130）用的**就是 `count == 3 && !expanded` 这条**，
                // 两侧口径一致；差别只在 GB 还多给 `2910.2` 与 >3 组分的 `FZ/T 01026`。
                //
                // 显微镜串不能在早退里被吞掉 —— 上面两支都成立：独占返回的那支自己带上它，
                // 累加的那支在末尾追加。
                if (count == 3 && !expanded)
                {
                    AddIfNotEmpty(parts, ternary);
                    if (hasCellulosicParent) AddIfNotEmpty(parts, microscope);
                    return string.Join(" ", parts);
                }

                parts.AddRange(subStandards);

                // 展开得来的三元：分部在前、`-2` 在后 —— 与 GB 侧 `…2910.20 …2910.2` 同序
                if (count == 3)
                    AddIfNotEmpty(parts, ternary);

                if (hasCellulosicParent)
                    AddIfNotEmpty(parts, microscope);
            }

            // ========== GB (FZ/T / GB/T) ==========
            if (isGb)
            {
                // FZ/T 01057 是**系列名** —— 报告上要落 .1~.4 四个分部，不是这一个系列名。
                // 其余 FZ/T / GB/T 串一律原样透传；**包括老记录的 "FZ/T 01057.1-4–2007"**：
                if (standard.Equals(FZ01057, StringComparison.OrdinalIgnoreCase))
                    parts.AddRange(Fz01057Series(mode));
                else
                    parts.Add(standard);

                // T129: 无 elastane → GB/T 2910.1（通则）。
                // >3 组分且无槽展开时改引 FZ/T 01026 那条线，2910 系列（通则也算）一条都不引。
                var hasElastane = fibers.Any(FiberTokens.IsElastane);
                if (!hasElastane && !(count > 3 && !expanded))
                    parts.Add(GB2910_1);

                // T130: GB 成分对 → GB 子标准
                // 三元及以上且无槽展开时不列 —— 与 ISO 侧的三元早退同口径（判据是槽展开，不是成分数）。
                if (!(count >= 3 && !expanded))
                {
                    var gbSubs = new HashSet<string>();
                    foreach (var (first, second) in EnumeratePairs(slots))
                    {
                        if (ResolveGbPart(first, second, hasAcrylic) is not int part) continue;

                        // 2C 下这个分部落空（`2910.22`）→ 跳过，不让它占位。
                        var s = GbPartOf(mode, part);
                        if (s.Length == 0) continue;

                        gbSubs.Add(s);
                    }
                    parts.AddRange(gbSubs);
                }

                // T131: 3组分 → GB/T 2910.2 / >3组分 → FZ/T 01026
                if (count == 3)
                    parts.Add(GB2910_2);
                else if (count > 3)
                    parts.Add(FZ01026);

                // cellulosic
                if (hasCellulosicParent)
                    parts.Add(MicroscopeGbChain(mode));

                // 单组分 = 定性鉴别报告，链里必须有显微法。
                // 勾 GB/T 2910.x 时上面一条显微法都不会产生（FZ/T 30003 那支要 cellulosic 父槽，
                // GB/T 2910.x 全是化学溶解定量法），故补 .3 这条定性显微法。
                //
                // 两道守卫，缺一不可：
                //   · hasIdentificationStd —— 分析员勾的**本身就是** FZ/T 01057 系列。
                //     除了系列名 `FZ/T 01057`（上面展开成 .1~.4），真实库里还有**合并写法**
                //     `FZ/T 01057.1-4–2007`（老记录，逐字透传），它同样已含 .3 ——
                //     只看 parts.Contains 挡不住它，会补成 `.1-4–2007 … .3-2025` 写两次。
                //   · parts.Contains(FZ01057_3) —— 系列展开那支落下的 .3，防重复。
                //
                // **只有单组分补**：多组分的显微法由 cellulosic 那支的 FZ/T 30003-2024 负责（麻棉混纺光学显微镜法定量）。
                //
                // 追加在**队尾**，与上一行的 FZ/T 30003 同序 —— GB 侧的惯例是"定量法在前、
                // 显微法收尾"（ISO 侧相反，是鉴别法领队，那是它自己的惯例，不互相统一）。
                //
                // ⚠️ **这一整段在单组分路径上走不到了** —— 单组分在方法入口就分流去了
                // BuildSingleComponentChain（那里有一份等价的 .3 补链）。这段代码保留是因为
                // 它就是多组分路径的完整形状，且 `isSingleComponent` 恒为 false 时逐字不变。
                if (isSingleComponent && !hasIdentificationStd && !parts.Contains(Fz01057Part(3, mode)))
                    parts.Add(Fz01057Part(3, mode));
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// **单组分**（<see cref="AnalysisType.Single"/>）的标准链 —— 只出「勾选的标准本身 + 鉴别法」。
        ///
        /// <para>
        /// 单组分表里每行是**不同测点下的各一种组分**（每行一种纤维、各 100%），
        /// 不是多组分那种"同一测点下的多种组分（一份混纺）"。GB/T 2910.x、ISO 1833-x
        /// 这些**定量**标准的前提是"一份混纺要拆"，对单组分不成立 ——
        /// 所以**凡是"按纤维列表/纤维对查表"的派生一律不做**：
        /// T129 补 GB/T 2910.1、T130 按纤维对查 GB 子标准、T131 三元/四元（2910.2 / FZ/T 01026）、
        /// cellulosic 父槽的 ISO 20705 / FZ/T 30003、以及 ISO 侧的 subStandards 与三元早退，
        /// 全部只在多组分路径生效。
        /// </para>
        ///
        /// <para>
        /// 单组分的 `Type == Single` 只是这个 bit，**不是**"纤维只有一条"：
        /// 单组分记录可以有 1 条也可以有多条单纤维。
        /// </para>
        ///
        /// <para>
        /// <b>鉴别法保留</b>：单组分报告本来就是定性鉴别报告。
        /// </para>
        /// </summary>
        /// <param name="standard">已由调用方判定为 ISO / DIN / EN / BS / GB（FZ/T、GB/T）之一。</param>
        /// <param name="family">
        /// 四族之一是 ISO 族，<c>null</c> = GB 族（调用方已保证二者必居其一）。
        /// </param>
        /// <param name="mode">
        /// 取值口径。单组分链里只有「鉴别法」那一条，而鉴别法**不分模式**（见
        /// <see cref="QualitativeFor"/>），所以对 ISO / DIN / EN / BS 四族这个参数**没有效果**；
        /// 只在 GB 侧生效 —— FZ/T 01057 系列的 `.2/.3` 退回 2007。
        /// </param>
        private static string BuildSingleComponentChain(
            string standard, StandardFamily? family, StandardMode mode)
        {
            // ISO / DIN / EN / BS：只有鉴别标准 —— 没有 subStandards、没有三元早退、没有 cellulosic 那串。
            if (family is StandardFamily f) return QualitativeFor(f);

            var parts = new List<string>();

            // FZ/T 01057 是**系列名** —— 报告上落 .1~.4 四个分部。
            // 其余 FZ/T / GB/T 串逐字透传，**含老记录的合并写法 `FZ/T 01057.1-4–2007`**
            // （它本身就含 .3）。这一条与多组分路径同构，是"勾选的标准本身"那一半。
            if (standard.Equals(FZ01057, StringComparison.OrdinalIgnoreCase))
                parts.AddRange(Fz01057Series(mode));
            else
                parts.Add(standard);

            // 勾的是**纯定量**标准（GB/T 2910.x）时，链里一个显微法都不会有
            // （FZ/T 30003 那支要 cellulosic 父槽，而它不对单组分生效），
            // 故补 .3 这条定性显微法。
            //
            // 两道守卫与多组分路径里那份同义，缺一不可：
            //   · hasIdentificationStd —— 勾的本身就是 FZ/T 01057 系列（含合并写法），已含 .3；
            //   · parts.Contains —— 系列展开那支已落下的 .3，防重复。
            //
            // 比对的是**本模式下的 .3**（2C 是 2007 版）—— 两处都是同一个 `Fz01057Part(3, mode)`，
            // 所以去重判定不会因模式而错位。
            var hasIdentificationStd = standard.StartsWith(FZ01057, StringComparison.OrdinalIgnoreCase);
            if (!hasIdentificationStd && !parts.Contains(Fz01057Part(3, mode)))
                parts.Add(Fz01057Part(3, mode));

            return string.Join(" ", parts);
        }

        /// <summary>
        /// 成分数 —— **父槽被其子纤维替换（1 换 N）**，不是顶层槽数、也不是父子都算；
        /// 且**同名纤维只算一个**。
        ///
        /// 去重走**比较器默认的 ordinal**，与上面那两处刻意一致：<c>LookupSubStandard</c> 自己会
        /// <c>ToLowerInvariant</c>，所以这个数的口径是"**不同写法**数"，不是"不同化学纤维数"。
        /// 别顺手改成大小写折叠 —— <c>QualitativeDedupTests</c> 钉着同一件事。
        ///
        /// **不是报告上显示的组分数**：那个数在模板里根本没有书签，是死写入。
        /// </summary>
        internal static int EffectiveComponentCount(IReadOnlyList<FiberSlot> slots)
            => slots.SelectMany(s => s.PairingNames).Distinct().Count();

        /// <summary>
        /// 参与查表的**有序对**枚举 —— 两段合起来：
        ///
        /// <list type="number">
        /// <item>**相邻对，取槽名** —— 与引入槽位通道之前逐字同源。父槽字面量仍作为配对元素出现在这里
        ///   （GetOrderedFiberNames() 里那个字面量没被拿掉），所以无子纤维的记录
        ///   第二段一次都不执行、输出**逐字不变**。</item>
        /// <item>**子纤维 × 其它槽** —— 只从**含子纤维的槽**出发，与其它每个槽的
        ///   <see cref="FiberSlot.PairingNames"/> **双向**配对。没有子纤维的槽之间不会凭空多出配对：
        ///   这是 C 与"全叉积"的分界，也是它只动 8 条、而全叉积要动 89 条的原因。</item>
        /// </list>
        ///
        /// 第 2 段为什么必须双向：查表函数按 (first, second) 有向，而"谁是前位纤维"
        /// 由前端拆分列/溶解列的表单结构决定，不是分析员的选择 —— 单向配会让同一对因录入顺序给不同分部
        /// （查表函数里那几处"任意顺序"的判定修的正是这一类）。子纤维的方向更无从谈起：它在表单里只有一个格子。
        /// </summary>
        internal static IEnumerable<(string First, string Second)> EnumeratePairs(IReadOnlyList<FiberSlot> slots)
        {
            for (int i = 0; i < slots.Count - 1; i++)
                yield return (slots[i].Name, slots[i + 1].Name);

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].SubFibers.Count == 0) continue;

                for (int j = 0; j < slots.Count; j++)
                {
                    if (i == j) continue;

                    foreach (var sub in slots[i].PairingNames)
                        foreach (var other in slots[j].PairingNames)
                        {
                            yield return (sub, other);
                            yield return (other, sub);
                        }
                }
            }
        }

        /// <summary>
        /// 对相邻成分对查表返回 ISO1833 子标准编号。
        /// <paramref name="hasAcrylic"/>：整条纤维列表里是否含丙烯腈类纤维，见类型注释的「丙烯腈守卫」。
        ///
        /// 只是 <see cref="ResolveIsoPart"/> + <see cref="RenderIsoSub"/> 的薄包装
        /// （ISO 族）—— 签名、返回值与拆分前逐字一致。
        /// </summary>
        internal static string LookupSubStandard(string first, string second, bool hasAcrylic)
            => LookupSubStandard(first, second, hasAcrylic, StandardMode.Current);

        /// <summary>同上，指定取值口径（2C golden 用；四参重载，三参那个 = 传 <see cref="StandardMode.Current"/>）。</summary>
        internal static string LookupSubStandard(
            string first, string second, bool hasAcrylic, StandardMode mode)
            => ResolveIsoPart(first, second, hasAcrylic) is int part
                ? RenderIsoSub(StandardFamily.Iso, mode, part)
                : string.Empty;

        /// <summary>
        /// 组分对 → **ISO 1833 分部号**（<c>null</c> = 落空）。
        ///
        /// 规则体与拆分前逐字同源，只有两处形态变化：<c>return ISO1833_20</c> 成了 <c>return 20</c>，
        /// 以及丙烯腈守卫那三处 <c>? ISO1833_12 : ISO1833_20</c> 成了分部号。
        /// **串不在这里给**（见 <see cref="IsoPartOf"/> / <see cref="RenderIsoSub"/>）。
        /// </summary>
        private static int? ResolveIsoPart(string first, string second, bool hasAcrylic)
        {
            var f = first?.Trim().ToLowerInvariant() ?? string.Empty;
            var s = second?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(s)) return null;

            // 纤维素 × 弹性纤维（**任意顺序**）→ -20
            // 这一条把两个方向并成一处：原先 (纤维素, Elastane) 返回 -1（1833-1 是"试验通则"，
            // 不是纤维对的定量方法），而反序 (Elastane, 纤维素) 走下面的 -12 —— 同一对因录入顺序给不同分部。
            // IsCellulosic 的成员全部落在 1833-20 的官方范围内。
            if ((FiberTokens.IsCellulosic(f) && FiberTokens.IsElastane(s))
             || (FiberTokens.IsElastane(f) && FiberTokens.IsCellulosic(s)))
                return hasAcrylic ? 12 : 20;

            // Silk + wool/cashmere → -18
            if (f == "silk" && (s == "wool" || s == "cashmere"))
                return 18;

            // wool/animal + any → -4
            if (FiberTokens.IsAnimal(f))
                return 4;

            // polyester + elastane（涤氨，任意顺序）→ -20（DMAc 法）
            if ((f == "polyester" && FiberTokens.IsElastane(s))
             || (FiberTokens.IsElastane(f) && s == "polyester"))
                return hasAcrylic ? 12 : 20;

            // polyamide/nylon + elastane（锦氨，任意顺序）→ -20（DMAc 法）
            if (((f == "polyamide" || f == "nylon") && FiberTokens.IsElastane(s))
             || (FiberTokens.IsElastane(f) && (s == "polyamide" || s == "nylon")))
                return hasAcrylic ? 12 : 20;

            // 弹性纤维在前 → -12（DMF 法），**但后位必须是"某些其他纤维"**（按官方范围收紧）。
            //
            // 收紧后两向都落空，与标准一致。够不够格由 FiberTokens.IsIso1833_12OtherFibre 判定，
            // 收法见那个集合的注释（只收官方逐字列到的名字）。
            //
            // 注意与前几条的分工：(Elastane, 纤维素) 与 (Elastane, Polyester) 走上面的 -20，
            // 根本到不了这里。(Elastane, Acrylic/Modacrylic) **落空** —— 弹性纤维与腈纶都是
            // -12 的**对象**纤维、都能被 DMF 溶掉，这一对分不出比例，-12 与 -20 都不适用。
            if (FiberTokens.IsElastane(f) && FiberTokens.IsIso1833_12OtherFibre(s))
                return 12;

            // polyamide/nylon + any → -7
            if (f == "polyamide" || f == "nylon")
                return 7;

            // 丙烯腈类在前 → -12（DMF 法）。谓词含 Modacrylic（1833-12 适用范围含 "certain modacrylics"）。
            //
            // **后位同样必须是"某些其他纤维"**（与上面弹性纤维兜底同族）。收紧后两向都落空，与标准一致。
            //
            // 够不够格由 FiberTokens.IsIso1833_12OtherFibre 判定（与弹性纤维兜底共用同一条，收法见那个集合的注释）。
            if (FiberTokens.IsAcrylicType(f) && FiberTokens.IsIso1833_12OtherFibre(s))
                return 12;

            // 再生纤维素 + linen/flax → -22（甲酸法）
            // **必须在 -6 之前**：原先排在 -6 后面，而 -6 的旧条件 IsCellulosic(f) && IsCellulosicOrCotton(s)
            // 对 (Viscose, Linen) 成立，于是 -22 永远轮不到、结构上不可达。
            // 补 flax 是因为库里 Flax / Linen 是同一纤维的两行；**不补 ramie**（1833-22:2020 官方范围只有 flax）。
            if (FiberTokens.IsRayonType(f) && (s == "linen" || s == "flax"))
                return 22;

            // 再生纤维素 + cotton 等 → -6（甲酸/氯化锌法）
            // 按官方适用范围收紧：对象纤维是 viscose/certain cupro/modal/lyocell（= IsRayonType），
            // 合作纤维是 cotton（该分部正是为它制定的）+ polypropylene / elastolefin / melamine。
            // **不含** linen / ramie / hemp / paper；而且 cotton 在这里是**合作方**、不是对象纤维。
            if (FiberTokens.IsRayonType(f) && (s == "cotton" || s == "polypropylene" || s == "elastolefin" || s == "melamine"))
                return 6;

            // cellulosic/cotton + elastomultiester/polyester → -11
            if (FiberTokens.IsCellulosicOrCotton(f) && (s == "elastomultiester" || s == "polyester"))
                return 11;

            // acetate alone → -3
            if (f == "acetate")
                return 3;

            return null;
        }

        /// <summary>
        /// GB版成分对→子标准号映射（数据库B列，Excel N129-N132）。
        /// <paramref name="hasAcrylic"/> 与 ISO 侧同一条守卫。
        ///
        /// 只是 <see cref="ResolveGbPart"/> + <see cref="GbPartOf"/> 的薄包装 ——
        /// 签名、返回值与拆分前逐字一致（含守卫生效时的空串）。
        /// </summary>
        internal static string LookupGbSubStandard(string first, string second, bool hasAcrylic)
            => LookupGbSubStandard(first, second, hasAcrylic, StandardMode.Current);

        /// <summary>同上，指定取值口径（2C golden 用）。</summary>
        internal static string LookupGbSubStandard(
            string first, string second, bool hasAcrylic, StandardMode mode)
            => ResolveGbPart(first, second, hasAcrylic) is int part
                ? GbPartOf(mode, part)
                : string.Empty;

        /// <summary>
        /// 组分对 → **GB/T 2910 分部号**（<c>null</c> = 落空，含丙烯腈守卫生效时的空串）。
        /// 规则体与拆分前逐字同源，串由 <see cref="GbPartOf"/> 给。
        /// </summary>
        private static int? ResolveGbPart(string first, string second, bool hasAcrylic)
        {
            var f = first?.Trim().ToLowerInvariant() ?? string.Empty;
            var s = second?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(s)) return null;

            // Silk + wool → GB/T 2910.18
            if (f == "silk" && s == "wool")
                return 18;

            // wool/Silk + 非spandex/elastane → GB/T 2910.4
            if (FiberTokens.IsAnimal(f) && s != "spandex" && s != "elastane")
                return 4;

            // polyamide/nylon + elastane（锦氨，任意顺序）→ GB/T 2910.20
            if (((f == "polyamide" || f == "nylon") && FiberTokens.IsElastane(s))
             || (FiberTokens.IsElastane(f) && (s == "polyamide" || s == "nylon")))
                return hasAcrylic ? null : 20;

            // polyamide/nylon + 非spandex/elastane → GB/T 2910.7
            if ((f == "polyamide" || f == "nylon") && s != "spandex" && s != "elastane")
                return 7;

            // 丙烯腈类在前 → GB/T 2910.12（谓词含 Modacrylic），后位同样必须是"某些其他纤维"
            //
            // **与 ISO 侧共用同一条判定**：GB/T 2910.12–2023 是 MOD ISO 1833-12:2020，其第二组分列表逐项一致 ——
            // 绵羊毛、其他动物毛、桑蚕丝、棉、粘胶、铜氨、莫代尔、莱赛尔、聚酰胺、聚酯、聚丙烯、
            // 聚酯复合弹性纤维、聚烯烃弹性纤维、三聚氰胺、聚丙烯/聚酰胺复合纤维、聚丙烯酸酯、玻璃。
            // 故不另建一份 GB 专用集合。
            if (FiberTokens.IsAcrylicType(f) && FiberTokens.IsIso1833_12OtherFibre(s))
                return 12;

            // rayon系 + cotton → GB/T 2910.6
            if (FiberTokens.IsRayonType(f) && s == "cotton")
                return 6;

            // 纤维素 × 弹性纤维（**任意顺序**）→ GB/T 2910.20 —— 与 ISO 侧同一条对称
            if ((FiberTokens.IsCellulosic(f) && FiberTokens.IsElastane(s))
             || (FiberTokens.IsElastane(f) && FiberTokens.IsCellulosic(s)))
                return hasAcrylic ? null : 20;

            // polyester + elastane（涤氨，任意顺序）→ GB/T 2910.20 —— 与 ISO 侧的涤氨规则逐字对称
            // 这一条取代了原先的 `if (f == "polyester") return GB2910_24;` 兜底：
            // 2910.24 的适用范围不是"任意后位纤维"，凡聚酯在前一律给它本身就是错的。
            if ((f == "polyester" && FiberTokens.IsElastane(s))
             || (FiberTokens.IsElastane(f) && s == "polyester"))
                return hasAcrylic ? null : 20;

            // cellulosic/cotton/acetate/linen/ramie + polyester → GB/T 2910.11
            if ((FiberTokens.IsCellulosicOrCotton(f) || f == "acetate" || f == "linen" || f == "ramie") && s == "polyester")
                return 11;

            // rayon系 + linen/ramie/flax → GB/T 2910.22
            // 中国版把苎麻扩了进来、ISO 侧没有（两侧刻意不对称）；而 `flax` 与 `linen`
            // 是 fiber_database 里**同一纤维的两行**（ISO 侧已补），这里必须同步补——
            // 否则同一纤维录成 Linen 给 2910.22、录成 Flax 就落空，结果由拼写决定。
            if (FiberTokens.IsRayonType(f) && (s == "linen" || s == "ramie" || s == "flax"))
                return 22;

            // acetate → GB/T 2910.3
            if (f == "acetate")
                return 3;

            return null;
        }
    }
}
