using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.DataSheetContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Util;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Domain.Services.DataSheetContext
{
    /// <summary>
    /// 数据单模型生成的领域规则实现。
    ///
    /// 设计约束（与同目录 <c>ConditionPoolDomainService</c> 保持一致）：
    /// 零仓储依赖、零基础设施依赖，全部方法为纯内存计算，可脱离数据库单测。
    /// 因此本类刻意<b>不</b>注入 <c>IStandardRepository</c> / <c>ITemplateSelectService</c>——
    /// 查标准、选模板属于应用层编排职责，所需数据由外部传入。
    /// </summary>
    public class DataSheetModelDomainService : IDataSheetModelDomainService, IScopedDependency
    {
        /// <summary>洗涤次数字段名（参数集中约定使用该键描述水洗次数序列）。</summary>
        private const string WashCycleKey = "WashCycle";

        /// <summary>
        /// 按 <c>TestPointParams</c> 的 ParamSet 值分组（分组键剔除 <c>WashCycle</c>），
        /// 输出测点参数分包。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <returns>分包列表；无参数配置时返回空列表。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> 为 null 时抛出。</exception>
        public IReadOnlyList<TestPointPackage> PackByParamSet(CheckListItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (item.TestPointParams == null || item.TestPointParams.Count == 0)
                return Array.Empty<TestPointPackage>();

            var packages = item.TestPointParams
                .GroupBy(kv =>
                {
                    if (kv.Value?.Values == null)
                        return "{}"; // 空参数集统一归入同一个空分组

                    // 克隆一份字典剔除 WashCycle 后作为分组键：
                    // 「水洗次数不同不影响参数合并」是业务口径，故 WashCycle 不参与判等。
                    var filteredValues = new Dictionary<string, object>();
                    foreach (var v in kv.Value.Values)
                    {
                        if (WashCycleKey.Equals(v.Key, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // JsonElement 必须取原始文本：其 ToString() 对字符串会丢引号，
                        // 导致 "1" 与 1 序列化后看起来相同，分组键会误合并。
                        filteredValues[v.Key] = v.Value is JsonElement je
                            ? je.GetRawText() ?? string.Empty
                            : v.Value ?? string.Empty;
                    }

                    // 沿用 Newtonsoft 序列化：需保证字典键顺序稳定，否则同一组参数会散成多组。
                    return JsonConvert.SerializeObject(filteredValues);
                })
                // 注意与上方的口径差异：分组「键」剔除了 WashCycle，但聚合「体」保留原始 ParamSet
                // （含 WashCycle），因为后续水洗展开仍需要它。
                .Select(g => new TestPointPackage(
                    TestPointIds: g.Select(x => x.Key).OrderBy(k => k).ToList(),
                    ParamSet: g.First().Value))
                // 按首个测点 ID 排序，保证输出顺序稳定（模板选择与书签顺序都依赖它）
                .OrderBy(p => p.TestPointIds.First())
                .ToList();

            return packages;
        }

        /// <summary>
        /// 计算分包所需的数据区格数。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <param name="package">参数分包。</param>
        /// <returns>数据区格数。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null 时抛出。</exception>
        public int CalculateDataAreaCount(CheckListItem item, TestPointPackage package)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (package == null) throw new ArgumentNullException(nameof(package));

            // 无参数配置时，每个测点占 1 个数据区
            if (item.TestPointParams == null || item.TestPointParams.Count == 0)
                return package.TestPointIds.Count;

            int totalCount = 0;

            foreach (var testPointId in package.TestPointIds)
            {
                if (!item.TestPointParams.TryGetValue(testPointId, out var paramSet) || paramSet?.Values == null)
                {
                    totalCount += 1; // 无参数配置，默认 1 个 Area
                    continue;
                }

                if (paramSet.Values.TryGetValue(WashCycleKey, out var washCycleObj) && washCycleObj is string washCycleStr)
                {
                    // 格式如 "1-2-32-45"：按 '-' 分割，段数即数据区个数
                    var washCount = washCycleStr.Split('-', StringSplitOptions.RemoveEmptyEntries).Length;
                    totalCount += washCount > 0 ? washCount : 1;
                }
                else
                {
                    totalCount += 1; // 非水洗扩展，每个测点占 1 个 Area
                }
            }

            return totalCount;
        }

        /// <summary>
        /// 按洗涤次数展开样本数据 / 洗后数据数组。
        /// 返回值长度与 <see cref="CalculateDataAreaCount"/> 的返回值恒等。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <param name="package">参数分包。</param>
        /// <param name="kind">
        /// 数据种类。它<b>不仅影响文档口径，也影响写入的值</b>：
        /// <c>Sample</c> 写入测点 ID（样本号），<c>AfterWash</c> 写入水洗次数值。
        /// </param>
        /// <returns>展开后的字符串数组。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null 时抛出。</exception>
        public string[] ExpandByWashCycle(CheckListItem item, TestPointPackage package, WashDataKind kind)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (package == null) throw new ArgumentNullException(nameof(package));

            // 无参数配置时：
            //   样本数据 → 直接返回测点 ID 数组（每个测点 1 个样本）
            //   洗后数据 → 每个测点默认水洗 1 次，故填充相同个数的 "1"
            if (item.TestPointParams == null || item.TestPointParams.Count == 0)
            {
                return kind == WashDataKind.Sample
                    ? package.TestPointIds.ToArray()
                    : Enumerable.Repeat("1", package.TestPointIds.Count).ToArray();
            }

            var expanded = new List<string>();

            foreach (var testPointId in package.TestPointIds)
            {
                if (!item.TestPointParams.TryGetValue(testPointId, out var paramSet) || paramSet?.Values == null)
                {
                    // 无参数配置：样本数据放测点 ID；洗后数据放默认水洗 1 次
                    expanded.Add(kind == WashDataKind.Sample ? testPointId : "1");
                    continue;
                }

                if (paramSet.Values.TryGetValue(WashCycleKey, out var washCycleObj) && washCycleObj is string washCycleStr)
                {
                    var washNumbers = washCycleStr.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    var repeatCount = washNumbers.Length > 0 ? washNumbers.Length : 1;

                    if (kind == WashDataKind.Sample)
                    {
                        // 样本数据：同一测点按水洗次数重复出现（模型里一个测点对应 N 个样本号）
                        for (int i = 0; i < repeatCount; i++)
                        {
                            expanded.Add(testPointId);
                        }
                    }
                    else
                    {
                        // 洗后数据：写入每个水洗次数值本身（如 1、2、32、45）
                        foreach (var washNumber in washNumbers)
                        {
                            expanded.Add(washNumber.Trim());
                        }
                    }
                }
                else
                {
                    // 非水洗扩展：样本数据放测点 ID；洗后数据放默认水洗 1 次
                    expanded.Add(kind == WashDataKind.Sample ? testPointId : "1");
                }
            }

            // 本方法产出的长度与 CalculateDataAreaCount 的返回值恒等，
            // 因此调用方（含 BuildBookmarkMap）不再二次调用 CalculateDataAreaCount ——
            // 两套同构算法并存会在日后一处口径变更时悄悄分叉，故统一以展开结果为准。
            return expanded.ToArray();
        }

        /// <summary>
        /// 构建书签映射，并判定是否需要复制整块 data 表。
        /// </summary>
        /// <param name="item">检测清单条目。</param>
        /// <param name="package">参数分包。</param>
        /// <param name="template">已选中的模板，提供数据区容量。</param>
        /// <param name="expanded">已展开的数组，直接使用不重复展开。</param>
        /// <param name="kind">数据种类，决定书签前缀与容量来源。</param>
        /// <returns>
        /// 书签映射聚合结果；模板不具备该数据区时返回 <see cref="BookmarkMapResult.Empty"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="template"/> 或 <paramref name="expanded"/> 为 null 时抛出。
        /// </exception>
        public BookmarkMapResult BuildBookmarkMap(
            CheckListItem item,
            TestPointPackage package,
            Template template,
            string[] expanded,
            WashDataKind kind)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (expanded == null) throw new ArgumentNullException(nameof(expanded));

            int capacity = kind == WashDataKind.Sample
                ? template.TemplateStructure.SampleDataAreaCount
                : template.TemplateStructure.AfterWashDataCount;

            // 两条路径的质量门顺序不同，刻意各自保留：
            //   Sample  —— 不按容量短路，容量未配置(0)时仍生成映射，仅 IsAppend 判为 false
            //   AfterWash —— 模板没这块数据区就直接返回空，连映射都不生成
            if (kind == WashDataKind.AfterWash && capacity <= 0)
                return BookmarkMapResult.Empty;

            // 没有数据（或展开后为空）时无需任何映射
            if (expanded.Length == 0)
                return BookmarkMapResult.Empty;

            var prefix = kind == WashDataKind.Sample ? "Sample_data_" : "After_wash_data_";

            var map = new Dictionary<string, string>(expanded.Length);
            for (int i = 0; i < expanded.Length; i++)
            {
                map[$"{prefix}{i + 1}"] = expanded[i];
            }

            // 需求口径：展开个数「超过」模板预留格数才需要复制整块 data 表（相等时刚好填满，不复制）。
            // 另：TemplateStructure 无初始化器、工厂只校验 < 0，未配置时容量为 0，
            // 若不先要求 capacity > 0 则比较会失效，故必须带上该防护。
            var isAppend = capacity > 0 && expanded.Length > capacity;

            return new BookmarkMapResult(map, isAppend);
        }
    }
}
