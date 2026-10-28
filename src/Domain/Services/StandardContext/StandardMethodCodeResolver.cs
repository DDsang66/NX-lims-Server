using System.Text.RegularExpressions;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.StandardContext;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Domain.Services.StandardContext
{
    /// <summary>
    /// <see cref="IStandardMethodCodeResolver"/> 的实现：把标准号归一化为「机构 + 编号 + 方法号」。
    ///
    /// 本类是**零依赖纯函数式领域服务**（与 <c>ConditionPoolDomainService</c> 的定位一致）：
    /// 不注入仓储、不查库、不做 IO，因此可以被任何层安全复用。
    ///
    /// 解析结果用于组装模板索引匹配条件（<c>runtimeConditions["StandardMethod"]</c>）。
    /// </summary>
    public class StandardMethodCodeResolver : IStandardMethodCodeResolver, IScopedDependency
    {
        /// <summary>
        /// 国家级/区域性「采纳前缀」。这些前缀只是把国际标准本地化，不改变标准的方法语义，
        /// 因此需要剥离，只保留主体机构（ISO / GB/T / AATCC / ASTM 等）。
        ///
        /// 注意：不包含 <c>ISO</c>——它通常就是主体机构；
        /// 也不包含需要与方法号一起解析的单词（如 <c>PART</c>），后者单独处理。
        /// </summary>
        private static readonly string[] AdoptionPrefixes =
        {
            "BS", "EN", "DIN", "NF", "SIS", "SS", "SANS", "UNI", "NEN", "SN", "AS", "JIS"
        };

        /// <summary>尾部版本年份，如 <c>:2020</c>、<c>-2017</c>。方法号只有 1~2 位，故 4 位数字可安全识别为年份。</summary>
        private static readonly Regex TrailingYearRegex = new(@"[:\-]\s*\d{4}\s*$", RegexOptions.Compiled);

        /// <summary>点号分隔的方法号，如 <c>12945.2</c> → <c>12945-2</c>。</summary>
        private static readonly Regex DottedMethodRegex = new(@"(\d)\s*\.\s*(\d)", RegexOptions.Compiled);

        /// <summary>英文写法的方法号，如 <c>Part 2</c> → <c>-2</c>。</summary>
        private static readonly Regex PartRegex = new(@"\s*PART\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>连续空白压缩为单个空格。</summary>
        private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);

        /// <inheritdoc />
        public string? Resolve(string? standardCode)
        {
            if (string.IsNullOrWhiteSpace(standardCode)) return null;

            var code = standardCode.Trim().ToUpperInvariant();

            // 1. 剥离尾部版本年份：ISO 12945-2:2020 → ISO 12945-2
            //    必须在剥离方法号之前做，否则 "12945-2" 里的 "-2" 虽不符合 4 位数字不会被误删，
            //    但 "12945.2-2017" 这种写法会残留成 "12945.2-"，后续分割会出错。
            code = TrailingYearRegex.Replace(code, string.Empty).Trim();

            // 2. 归一方法号写法：12945.2 → 12945-2；Part 2 → -2
            code = DottedMethodRegex.Replace(code, "$1-$2");
            code = PartRegex.Replace(code, "-$1");

            // 3. 循环剥离采纳前缀：BS EN ISO 12945-2 → EN ISO 12945-2 → ISO 12945-2
            //    用循环而非一次性替换，是因为 "BS EN ..." 这类复合前缀需要逐层剥。
            bool stripped;
            do
            {
                stripped = false;
                foreach (var prefix in AdoptionPrefixes)
                {
                    // 必须带分隔空格，避免把 "SANS" 误当 "SN"、"ASTM" 误当 "AS"
                    if (code.StartsWith(prefix + " ", StringComparison.Ordinal))
                    {
                        code = code.Substring(prefix.Length).TrimStart();
                        stripped = true;
                        break;
                    }
                }
            }
            while (stripped);

            // 4. 压缩空白并校正分隔符周边的空格："GB/T  12945 - 2" → "GB/T 12945-2"
            code = MultiSpaceRegex.Replace(code, " ").Trim();
            code = Regex.Replace(code, @"\s*-\s*", "-");

            // 5. 不含任何数字说明不是标准号（如 "MARTINDALE"、纯文字描述），无法解析
            if (!Regex.IsMatch(code, @"\d")) return null;

            return code;
        }
    }
}
