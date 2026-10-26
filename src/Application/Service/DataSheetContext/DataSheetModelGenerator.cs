using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext;
using NX_lims_Softlines_Command_System.src.Application.Interface;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ParamRuleContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.DataSheetContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Util;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using System.Text.Json;

namespace NX_lims_Softlines_Command_System.src.Application.Service.DataSheetContext
{
    /// <summary>
    /// 数据单模型生成的<b>应用层编排薄壳</b>。
    ///
    /// 职责边界：
    /// <list type="bullet">
    /// <item>留在本类：取条件池、选池、组装 <see cref="DataSheetModel"/>（含 ReportNumber / SheetCount 的取值降级）、
    /// 把领域层与公共组件的结果适配为实体契约。</item>
    /// <item>模板索引匹配与 testcondition 文本填充：见 <see cref="ITestConditionComposer"/>
    /// （<c>src\Application\Service\TemplateConditionContext</c>），与 checklist 侧共用同一份实现。</item>
    /// <item>纯计算规则（分包 / 数据区计数 / 水洗展开 / 书签映射）：见 <see cref="IDataSheetModelDomainService"/>
    /// （<c>src\Domain\Services\DataSheetContext</c>）。</item>
    /// </list>
    ///
    /// 本类不得再自行实现上述任何规则 —— 否则多套同构实现会在日后分叉。
    /// </summary>
    public class DataSheetModelGenerator : IScopedDependency, IDataSheetModelGenerator
    {
        /// <summary>testcondition 文本合成器（模板选择 + 文本填充），与 checklist 侧共用。</summary>
        private readonly ITestConditionComposer _composer;

        /// <summary>数据单模型领域规则（分包 / 计数 / 展开 / 书签映射）。</summary>
        private readonly IDataSheetModelDomainService _domainService;

        /// <summary>
        /// 构造薄壳。
        /// </summary>
        /// <param name="composer">testcondition 合成器，承载运行条件组装、模板选择与文本填充。</param>
        /// <param name="domainService">领域规则服务，承载全部纯计算逻辑。</param>
        /// <exception cref="ArgumentNullException">任一依赖为 null 时抛出。</exception>
        public DataSheetModelGenerator(
            ITestConditionComposer composer,
            IDataSheetModelDomainService domainService)
        {
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
            _domainService = domainService ?? throw new ArgumentNullException(nameof(domainService));
        }

        /// <summary>
        /// 按照 testPointParams 的 ParamSet 进行分组，生成 DataSheetModel 列表。
        ///
        /// 编排顺序：分包 → 逐包选池 → 调公共组件合成条件与模板 → 组装模型。
        /// </summary>
        /// <param name="pools">该 CheckList 下的全部条件池（由 Worker 批量预取，避免 N+1）。</param>
        /// <param name="item">本次要生成的检测项。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>模型列表；无分包或参数包匹配不到条件池时返回空列表。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 模板索引未命中（TC004）或模板缺少文本模板（TC005）时由公共组件抛出，本类不吞异常。
        /// </exception>
        public async Task<List<DataSheetModel>> GenerateAsync(
            List<ConditionPool> pools,
            CheckListItem item,
            CancellationToken ct)
        {
            var packages = _domainService.PackByParamSet(item);

            var models = new List<DataSheetModel>(packages.Count);

            foreach (var package in packages)
            {
                var target = package.TestPointIds.ToHashSet();

                var pool = pools.FirstOrDefault(p =>
                    p.TestPoints != null &&
                    target.All(x => p.TestPoints.Contains(x)));

                if (pool == null) continue; // 防御性编程，避免后续空引用

                // 运行条件组装 / 查标准拼 TestMethod / 模板索引匹配 / 文本模板填充，
                // 全部交给公共组件，与 checklist 侧共用同一份实现。
                var composition = await _composer.ComposeForPackageAsync(item, pool, package, ct);

                var template = composition.Template;

                // TestMethod 由公共组件写进运行时条件字典（已按 Id 排序，避免索引匹配漂移），
                // 这里直接复用，不再自行查标准库，避免第二份拼接逻辑。
                var testMethod = composition.RuntimeConditions.TryGetValue("TestMethod", out var testMethodValue)
                    ? testMethodValue?.ToString() ?? string.Empty
                    : string.Empty;

                // 规则计算统一委托领域服务（同步调用，不存在 async 无 await 的方法）
                var dataAreaCount = _domainService.CalculateDataAreaCount(item, package);

                var sampleExpanded = _domainService.ExpandByWashCycle(item, package, WashDataKind.Sample);
                var sampleResult = _domainService.BuildBookmarkMap(item, package, template, sampleExpanded, WashDataKind.Sample);

                var afterWashExpanded = _domainService.ExpandByWashCycle(item, package, WashDataKind.AfterWash);
                var afterWashResult = _domainService.BuildBookmarkMap(item, package, template, afterWashExpanded, WashDataKind.AfterWash);

                var model = new DataSheetModel
                {
                    ReportNumber = pool.Conditions.TryGetValue("ReportNo", out var rnVal)
                    ? ExtractString(rnVal)
                    : string.Empty,

                    SheetCount = pool.Conditions.TryGetValue("Copy", out var cpVal)
                    ? ExtractInt(cpVal, 1)
                    : 1,

                    DataAreaCount = dataAreaCount,

                    TemplateUrl = template.GetTemplateUrl(),

                    TestItem = item.TestItemId,

                    TestMethod = testMethod,

                    TestCondition = composition.Text,

                    SampleMap = ToSampleMap(sampleResult),

                    AfterWashMap = ToAfterWashMap(afterWashResult)
                };
                models.Add(model);
            }

            return models;
        }

        /// <summary>
        /// 把领域层的书签映射结果适配为实体契约 <see cref="SampleMap"/>。
        /// </summary>
        /// <param name="result">领域服务返回的书签映射聚合结果。</param>
        /// <returns>模板填充引擎可直接消费的 <see cref="SampleMap"/>。</returns>
        private static SampleMap ToSampleMap(BookmarkMapResult result)
        {
            return new SampleMap
            {
                SampleMetaData = new Dictionary<string, string>(result.Map),
                IsAppend = result.IsAppend
            };
        }

        /// <summary>
        /// 把领域层的书签映射结果适配为实体契约 <see cref="AfterWashMap"/>。
        /// </summary>
        /// <param name="result">领域服务返回的书签映射聚合结果。</param>
        /// <returns>模板填充引擎可直接消费的 <see cref="AfterWashMap"/>。</returns>
        private static AfterWashMap ToAfterWashMap(BookmarkMapResult result)
        {
            return new AfterWashMap
            {
                AfterWashMetaData = new Dictionary<string, string>(result.Map),
                IsAppend = result.IsAppend
            };
        }

        /// <summary>
        /// 从条件值中安全提取字符串。支持 <see cref="JsonElement"/>、<see cref="ParamValue"/> 与原生类型。
        ///
        /// 说明：本方法与 <see cref="ExtractInt"/> 服务于 <see cref="DataSheetModel"/> 的
        /// ReportNumber / SheetCount 取值降级，属应用层组装职责，故不下沉领域层。
        /// </summary>
        /// <param name="raw">原始条件值，可为 null。</param>
        /// <returns>提取到的字符串；无法识别或为 null 时返回空字符串。</returns>
        private static string ExtractString(object? raw)
        {
            if (raw == null) return string.Empty;

            switch (raw)
            {
                case JsonElement je:
                    return je.ValueKind switch
                    {
                        JsonValueKind.String => je.GetString() ?? string.Empty,
                        JsonValueKind.Number => je.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => string.Empty,
                        JsonValueKind.Undefined => string.Empty,
                        _ => je.GetRawText()
                    };

                case ParamValue pv:
                    return pv.Value?.ToString() ?? string.Empty;

                default:
                    return raw.ToString() ?? string.Empty;
            }
        }

        /// <summary>
        /// 从条件值中安全提取整数。支持 <see cref="JsonElement"/>、<see cref="ParamValue"/> 与原生数值类型。
        /// </summary>
        /// <param name="raw">原始条件值，可为 null。</param>
        /// <param name="defaultValue">无法解析时使用的默认值。</param>
        /// <returns>提取到的整数；无法解析或为 null 时返回 <paramref name="defaultValue"/>。</returns>
        private static int ExtractInt(object? raw, int defaultValue = 1)
        {
            if (raw == null) return defaultValue;

            switch (raw)
            {
                case JsonElement je:
                    if (je.ValueKind == JsonValueKind.Number)
                        return je.TryGetInt32(out var n) ? n : defaultValue;
                    if (je.ValueKind == JsonValueKind.String
                        && int.TryParse(je.GetString(), out var s))
                        return s;
                    return defaultValue;

                case ParamValue pv:
                    return ExtractInt(pv.Value, defaultValue);

                case int i: return i;
                case long l: return (int)l;
                case double d: return (int)d;

                default:
                    return int.TryParse(raw.ToString(), out var parsed) ? parsed : defaultValue;
            }
        }
    }
}
