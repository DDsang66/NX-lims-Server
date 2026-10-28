using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext.Enums;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Domain.Share.Enums;
using System.Text.Json;

namespace NX_lims_Softlines_Command_System.src.Domain.Services
{
    public class TemplateSelectService: ITemplateSelectService, IScopedDependency
    {
        private readonly ITemplateRepository _templateRepository;

        public TemplateSelectService(ITemplateRepository templateRepository)
        {
            _templateRepository = templateRepository ?? throw new ArgumentNullException(nameof(templateRepository));
        }

        public Template? FindByIndex(
            IReadOnlyDictionary<string, object?> conditions,
            string? preFilterKey = null)
        {
            if (conditions == null || conditions.Count == 0)
                throw new ArgumentException("查询条件不能为空", nameof(conditions));

            // 1. 决定用哪个 key 粗筛
            KeyValuePair<string, object?> preFilter;
            if (!string.IsNullOrWhiteSpace(preFilterKey))
            {
                if (!conditions.TryGetValue(preFilterKey, out var preValue))
                    throw new ArgumentException(
                        $"粗筛 key '{preFilterKey}' 不在 conditions 里", nameof(preFilterKey));

                preFilter = new KeyValuePair<string, object?>(preFilterKey, preValue);
            }
            else
            {
                preFilter = conditions.First();   // 兜底：不传时用第一个
            }

            // 2. 下推到 DB 粗筛
            var candidates = _templateRepository.FindByIndexKey(preFilter.Key, preFilter.Value);

            // 3. 内存完整匹配
            var matched = candidates
                .Where(t => t.Status == TemplateStatus.Published)
                .Where(t => t.TemplateIndex != null)
                .Where(t => MatchesAll(t.TemplateIndex, conditions))
                .ToList();

            // 4. 处理结果
            if (matched.Count == 0)
                return null;

            if (matched.Count > 1)
            {
                // 冲突时打印详情，方便排查数据问题
                var conflictDetail = string.Join(" | ", matched.Select(t =>
                    $"{t.Id.Value}[{string.Join(",", t.TemplateIndex!.Values.Select(v => $"{v.Key}={v.Value}"))}]"));

                throw new InvalidOperationException(
                    $"索引条件命中了 {matched.Count} 个模板，无法唯一确定。" +
                    $"粗筛key={preFilter.Key}，条件：{FormatConditions(conditions)}。" +
                    $"冲突模板：{conflictDetail}");
            }

            return matched[0];
        }

        public Template? FindByUrl(string templateUrl)
        {
            if (string.IsNullOrWhiteSpace(templateUrl))
                throw new ArgumentException("模板URL不能为空", nameof(templateUrl));

            return _templateRepository.FindByUrl(templateUrl);
        }

        /// <summary>
        /// 判断模板索引是否满足所有查询条件
        /// </summary>
        private static bool MatchesAll(TemplateIndex index, IReadOnlyDictionary<string, object?> conditions)
        {
            foreach (var kvp in conditions)
            {
                // 模板索引里没有这个 key → 跳过，不当作不匹配
                if (!index.Values.TryGetValue(kvp.Key, out var indexValue))
                    continue;

                if (!AreEqual(indexValue, kvp.Value))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 值比较：处理大小写、类型转换（如字符串 "4N" vs 数字 4）
        /// </summary>
        private static bool AreEqual(object? a, object? b)
        {
            // ★ 先归一化 JsonElement
            a = NormalizeJsonElement(a);
            b = NormalizeJsonElement(b);

            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            if (a is string sa && b is string sb)
                return string.Equals(sa.Trim(), sb.Trim(), StringComparison.OrdinalIgnoreCase);

            if (IsNumeric(a) && IsNumeric(b))
                return Convert.ToDecimal(a) == Convert.ToDecimal(b);

            return a.Equals(b);
        }

        private static object? NormalizeJsonElement(object? value)
        {
            if (value is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => je.GetRawText()
                };
            }
            return value;
        }

        private static bool IsNumeric(object value)
        {
            return value is byte or sbyte or short or ushort or int or uint
                or long or ulong or float or double or decimal;
        }

        private static string FormatConditions(IReadOnlyDictionary<string, object?> conditions)
        {
            return string.Join(", ", conditions.Select(kv => $"{kv.Key}={kv.Value}"));
        }
    }
}
