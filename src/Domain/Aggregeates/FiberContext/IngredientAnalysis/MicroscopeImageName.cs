using System;
using System.Collections.Generic;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis
{
    /// <summary>
    /// 显微镜图库的**别名表** —— 把录入名换到图库里实际存在的那个文件名。
    ///
    /// **为什么需要**：图库是按纤维名取文件的（<c>WordTemplateEngine.InsertMicroscopeImages</c>
    /// 里依次试 `<c>{fiberName}.png / .jpg / .jpeg</c>`），而 `fiber_database` 里
    /// **同一纤维有多行**，图库里却通常只有其中一行对应的那张图。
    /// 按另一行录入的记录就取不到图 —— 而且不是"图空着"，是整条 `continue`：
    /// **连图注都不印**，后面的纤维还会往前串一格。
    ///
    /// ⚠️ **这是一张显式白名单，不是"同义就收"**。<c>Rayon</c>（= Viscose）在
    /// <see cref="FiberTokens"/> 的口径里同样是同义行，
    /// 但是目前**不收**，所以它**刻意不在这里**。
    /// 别按"同义就加"扩表 —— 要加先问。
    ///
    /// ⚠️ **右侧不一定是另一行纤维**：<c>Rabbit</c> 既不在 `fiber_database`、也不在任何记录里，
    /// 它**只是 `MicroscopeImages/Rabbit.png` 的文件名**。这张表是「录入名 → 图库文件」的对照，
    /// 不是"同义纤维两行"的对照。
    ///
    /// **只用于找文件**：图片下方那行说明文字印的仍是录入名（见 InsertMicroscopeImages 里
    /// 的 <c>new Text(fiberName)</c>），所以报告上的纤维名不因本表而改变。
    /// </summary>
    internal static class MicroscopeImageName
    {
        /// <summary>录入名 → 图库里的实际文件名（不含扩展名）。</summary>
        private static readonly Dictionary<string, string> Aliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Spandex"] = "Elastane",
                ["Flax"] = "Linen",
                ["Nylon"] = "Polyamide",
                ["Rabbit hair"] = "Rabbit",
                // 图库文件名是「涤锦复合丝」，父槽的两个写法都指过去
                ["Bicomponent Fiber"] = "涤锦复合丝",
                ["Biconstituent Fiber"] = "涤锦复合丝",
                ["Olefin"] = "Polypropylene",
            };

        /// <summary>
        /// 取图库文件名。命中别名返回图库主名（返回规范写法）；未命中**原值返回** —— 所以除表里这几条外，全部输出逐字不变。
        /// </summary>
        internal static string Resolve(string fiberName)
            => fiberName != null && Aliases.TryGetValue(fiberName.Trim(), out var canonical)
                ? canonical
                : fiberName;

        /// <summary>别名表里的全部目标文件名。供测试逐条核实磁盘上确有这张图。</summary>
        internal static IReadOnlyCollection<string> AliasTargets => Aliases.Values;
    }
}
