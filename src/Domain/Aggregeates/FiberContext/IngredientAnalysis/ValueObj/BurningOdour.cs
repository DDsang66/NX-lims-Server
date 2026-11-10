using System;
using System.Collections.Generic;
using System.Linq;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.ValueObj
{
    /// <summary>
    /// 燃烧法 —— 每种纤维**燃烧时的气味**，抄自 ISO/TR 11827:2012 附录 A 表 A.1
    /// 「Characteristics relative to fibre identification testing」的 <c>odour</c> 列。
    ///
    /// <para>
    /// 表 A.1 是**一纤维一行**：接近火焰 / 在火焰中 / 离开火焰 / **气味** / 灰烬，外加含氯、含氮、
    /// 显微外观、碘-碘化钾染色、黄蛋白反应。全表 21 行，**没有** Paper / Feather / Black Smoke
    /// 这样的分类 —— 那是 burn-test 的速记法，不是标准原文。旧实现（<c>BurningCategory</c>）
    /// 把 14 种合成纤维压进 Black Smoke，等于把"气味"这一列整列丢掉：
    /// 标准里真写着冒黑烟的只有 PVC、偏氯乙烯(PVDC)、改性腈，而涤纶是"淡甜味 + 黑色硬圆粒"、
    /// 锦纶是"酰胺味"、氨纶是"特殊气味 + 橡胶状粘块"、醋酯是"醋酸味"。
    /// </para>
    ///
    /// <para>
    /// 核对用的是 ГОСТ Р 56561-2015（ISO/TR 11827:2012 的等同采用版，表 A.1 在第 27–31 页，
    /// 公开全文可查）。气味词按该表译成英文短语，不改措辞含义。
    /// </para>
    ///
    /// <para>
    /// <b>查不到的名字静默丢弃</b>（与旧实现一致）：金属纤维族（Metal fibre / Metallic /
    /// Metallised / Metallized）表 A.1 本就没有行，读数就是"不燃烧、无气味"，故刻意不给条目。
    /// 复合纤维的两个占位名（Bicomponent Fiber / Biconstituent Fiber）也不在此表 ——
    /// 调用方应先把它们**下钻到组分子纤维**再传进来（见 <c>IngredientAnalysisCalculation.GetOrderedFiberNamesForBurning</c>）。
    /// </para>
    ///
    /// <para>
    /// 输出形制由 <see cref="AnalysisResult.WithBurningTest"/> 拼成
    /// <c>Cotton: Burnt paper, Polyester: Faint sweet aroma</c> —— 逗号分隔、**不换行**（用户 2026-11-10 裁定）。
    /// </para>
    /// </summary>
    public static class BurningOdour
    {
        /// <summary>纤维名 → 燃烧气味。大小写不敏感，入口统一 Trim。</summary>
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── 烧纸味：纤维素纤维 ────────────────────────────────────────────
            // 表 A.1 有独立行：棉、大麻、亚麻、苎麻、粘胶、铜氨/高湿模量/莱赛尔（"同粘胶"）。
            // 其余是表里没有、按同类推定（同属纤维素，气味一致）。
            ["Cotton"] = "Burnt paper",
            ["Hemp"] = "Burnt paper",
            ["Flax"] = "Burnt paper",
            ["Linen"] = "Burnt paper",
            ["Ramie"] = "Burnt paper",
            ["Viscose"] = "Burnt paper",
            ["Rayon"] = "Burnt paper",
            ["Lyocell"] = "Burnt paper",
            ["Cupro"] = "Burnt paper",
            ["Jute"] = "Burnt paper",
            ["Modal"] = "Burnt paper",
            ["Paper Yarn"] = "Burnt paper",
            ["vegetable fibres"] = "Burnt paper",
            ["*cellulosic fibre"] = "Burnt paper",
            ["*Regenerated cellulose fibre"] = "Burnt paper",

            // ── 烧毛发味：蛋白质纤维 ──────────────────────────────────────────
            // 表 A.1 有独立行：蚕丝、羊毛；其余按同类推定。
            ["Silk"] = "Burnt hair",
            ["Wool"] = "Burnt hair",
            ["Tussah"] = "Burnt hair",
            ["Mohair"] = "Burnt hair",
            ["Cashmere"] = "Burnt hair",
            ["Alpaca"] = "Burnt hair",
            ["Rabbit hair"] = "Burnt hair",
            ["*Animal"] = "Burnt hair",

            // ── 醋酸味 ────────────────────────────────────────────────────────
            ["Acetate"] = "Acetic acid",
            ["Triacetate"] = "Acetic acid",

            // ── 酰胺味 ────────────────────────────────────────────────────────
            ["Polyamide"] = "Amide",
            ["Nylon"] = "Amide",

            // ── 甜味 / 烤肉味 / 烧皂味 ────────────────────────────────────────
            ["Polyester"] = "Faint sweet aroma",
            ["Acrylic"] = "Baked meat",
            ["Modacrylic"] = "Burning soap",

            // ── 石蜡味 ────────────────────────────────────────────────────────
            ["Polypropylene"] = "Burning paraffin",
            ["Polyethylene"] = "Burning paraffin",
            ["Olefin"] = "Burning paraffin",

            // ── 特殊气味：弹性纤维 ────────────────────────────────────────────
            ["Elastane"] = "Very unusual",
            ["Spandex"] = "Very unusual",
            ["Polyurethane"] = "Very unusual",
            ["Rubber"] = "Very unusual",
            ["Elastodiene"] = "Very unusual",
            ["Elastomultiester"] = "Very unusual",

            // ── 表 A.1 有、fiber_database 目前没有这几种 ──────────────────────
            // 先落表是为了让映射与标准对齐；下拉里选不到它们，加不加都不影响现有记录。
            ["PVC"] = "Sharp pungent",
            ["Vinylidene"] = "Sharp pungent",
            ["Vinal"] = "Sweetish, of burning PVA",
            ["Aramid"] = "Sweetish, pungent",
            ["Polylactide"] = "Aromatic, sweet",
        };

        /// <summary>
        /// 把纤维名单换成 <c>"纤维名: 气味词"</c> 列表：保留首次出现顺序、**同名只留一条**、
        /// 查不到的名字丢弃（不占位）。空名/空白名不算。
        ///
        /// 去重用 <see cref="StringComparer.OrdinalIgnoreCase"/>，与查表的比较器一致 ——
        /// 否则同一份称量表里既有 `Hemp` 又有 `hemp` 时会印两遍同一句气味。
        /// 印出来的是**录入的写法**（只 Trim，不改大小写、不换算显示名）。
        /// </summary>
        public static List<string> Describe(IEnumerable<string> fiberNames)
        {
            return fiberNames
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Map.ContainsKey)
                .Select(f => $"{f}: {Map[f]}")
                .ToList();
        }
    }
}
