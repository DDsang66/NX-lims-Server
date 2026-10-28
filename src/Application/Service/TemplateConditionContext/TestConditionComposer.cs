using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext;
using NX_lims_Softlines_Command_System.src.Application.Interface;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.Standard.ValueObj;
using NX_lims_Softlines_Command_System.Domain.Aggregeates.StandardContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.DataSheetContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.StandardContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Util;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Service;
using NX_lims_Softlines_Command_System.src.Infrastructure.TemplateEngine;

namespace NX_lims_Softlines_Command_System.src.Application.Service.TemplateConditionContext
{
    /// <summary>
    /// testcondition 文本合成器的唯一实现。
    ///
    /// 本类承载原先内联在 <c>DataSheetModelGenerator.GenerateAsync</c> 里的三段逻辑：
    /// 运行时条件组装、模板索引匹配、文本模板填充。
    /// 抽出的目的有两个：
    /// 1. datasheet 与 checklist 两条链路共用同一份实现，避免口径分叉；
    /// 2. 让「模板匹配用运行时条件、文本模板匹配用原始条件」这个易错点
    ///    由方法签名强制约束，调用方无法把它俩混用。
    /// </summary>
    public class TestConditionComposer : IScopedDependency, ITestConditionComposer
    {
        /// <summary>标准库查询，用于把 StandardIds 拼成 TestMethod 展示串。</summary>
        private readonly IStandardRepository _standardRepository;

        /// <summary>模板选择服务，按运行时条件做索引匹配。</summary>
        private readonly ITemplateSelectService _templateSelectService;

        /// <summary>数据单模型领域服务，复用其 PackByParamSet 做参数分包（纯算法，零仓储依赖）。</summary>
        private readonly IDataSheetModelDomainService _domainService;

        /// <summary>标准方法号解析器：把具体标准号归纳成方法级键（如 ISO 12945-2），供模板按方法绑定。</summary>
        private readonly IStandardMethodCodeResolver _methodCodeResolver;

        /// <summary>模板索引键：检测项标识（也是 FindByIndex 的初筛键）。</summary>
        private const string TestItemIdKey = "TestItemId";

        /// <summary>模板索引键：标准展示串（以 ";" 拼接）。</summary>
        private const string TestMethodKey = "TestMethod";

        /// <summary>
        /// 模板索引键：标准方法级键。由 <see cref="IStandardMethodCodeResolver"/> 归一化得到，
        /// 使同一测试方法的各变体标准号（ISO / EN ISO / BS EN ISO）能命中同一模板。
        /// </summary>
        private const string StandardMethodKey = "StandardMethod";

        /// <summary>
        /// 序列化配置，与 DataSheetModelGenerator 原实现保持一致。
        /// TypeConverter 用于把 object 类型的参数值安全序列化，避免出现 CLR 类型名。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions =
            new() { Converters = { new TypeConverter() } };

        /// <summary>
        /// 构造 testcondition 合成器。
        /// </summary>
        /// <param name="standardRepository">标准库仓储，用于拼接 TestMethod。</param>
        /// <param name="templateSelectService">模板选择服务。</param>
        /// <param name="domainService">数据单模型领域服务，提供参数分包。</param>
        /// <param name="methodCodeResolver">标准方法号解析器，用于注入 StandardMethod 条件键。</param>
        /// <exception cref="ArgumentNullException">任一依赖为 null 时抛出。</exception>
        public TestConditionComposer(
            IStandardRepository standardRepository,
            ITemplateSelectService templateSelectService,
            IDataSheetModelDomainService domainService,
            IStandardMethodCodeResolver methodCodeResolver)
        {
            _standardRepository = standardRepository ?? throw new ArgumentNullException(nameof(standardRepository));
            _templateSelectService = templateSelectService ?? throw new ArgumentNullException(nameof(templateSelectService));
            _domainService = domainService ?? throw new ArgumentNullException(nameof(domainService));
            _methodCodeResolver = methodCodeResolver ?? throw new ArgumentNullException(nameof(methodCodeResolver));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<string, object?>> BuildRuntimeConditionsAsync(
            ConditionPool pool,
            ParamSet? paramSet,
            CheckListItem item,
            CancellationToken ct)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (item == null) throw new ArgumentNullException(nameof(item));

            // 1. 克隆池条件：不能直接改写 pool.Conditions —— 它是 EF 跟踪实体的字典，
            //    写入运行时临时键会污染同批次其它参数包，也会在被保存时产生脏数据。
            var runtime = new Dictionary<string, object?>(pool.Conditions);

            // 2. 合并本分包的参数集，同名键以参数集为准
            if (paramSet?.Values != null)
            {
                foreach (var kv in paramSet.Values)
                {
                    runtime[kv.Key] = kv.Value;
                }
            }

            // 3. 查一次标准库：TestMethod 与 StandardMethod 共用同一批 Standard 实体。
            //    绝不能分两次查 —— 那会让每个参数包多一次 DB 往返。
            var standards = await LoadStandardsAsync(item, ct);

            // 4. 追加 TestItemId / TestMethod。
            //    TestItemId 是模板索引的初筛键；TestMethod 参与 MatchesAll 的逐字比对。
            runtime[TestItemIdKey] = item.TestItemId!.Value;
            runtime[TestMethodKey] = BuildTestMethodString(item, standards);

            // 5. StandardMethod：标准方法级键（如 ISO 12945-2），
            //    让模板可以按「测试方法」而不是「具体标准号」绑定，
            //    从而兼容 ISO / EN ISO / BS EN ISO 等各种写法变体。
            //    仅在恰好一个标准时注入：多标准场景下应拼成什么键尚无业务共识，
            //    强行拼接会重新引入「顺序敏感 + 组合爆炸」，与本次要解决的问题同类，
            //    因此宁可不注入（模型退化为只用 TestMethod 匹配），详见本目录 AGENTS.md 待确认项。
            if (standards.Count == 1)
            {
                var methodCode = _methodCodeResolver.Resolve(standards[0].StandardCode);
                if (!string.IsNullOrWhiteSpace(methodCode))
                {
                    runtime[StandardMethodKey] = methodCode;
                }
            }

            return runtime;
        }

        /// <inheritdoc />
        public Template ResolveTemplate(IReadOnlyDictionary<string, object?> runtimeConditions)
        {
            if (runtimeConditions == null) throw new ArgumentNullException(nameof(runtimeConditions));

            // preFilterKey 固定为 "TestItemId"：这是模板索引的初筛口径，
            // 与 datasheet 侧历史行为一致，不随调用场景变化，故不暴露为参数。
            var template = _templateSelectService.FindByIndex(runtimeConditions, preFilterKey: "TestItemId");
            if (template == null)
            {
                throw new TestConditionCompositionException(
                    TestConditionErrorCodes.TemplateNotMatched,
                    $"未找到匹配的模板：TestItemId={runtimeConditions.GetValueOrDefault("TestItemId")}，" +
                    $"TestMethod={runtimeConditions.GetValueOrDefault("TestMethod")}",
                    detail: "请检查该检测项/标准组合是否已被模板索引条件覆盖，或模板是否已发布。");
            }

            return template;
        }

        /// <inheritdoc />
        public string ComposeText(Template template, ConditionPool pool, ParamSet? paramSet)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            // 模板结构声明了不需要 testcondition（TestConditionCount <= 0）时直接返回空串，
            // 不抛 TC005。口径与发布校验保持一致：WordTemplateValidator.cs:88 只有
            // TestConditionCount > 0 才要求 docx 含 TestCondition 书签；此前的实现强制要求
            // 文本模板，会让「声明不需要 testcondition」的模板直接生成失败。
            // 零副作用：DataSheetFillingEngine.ReplaceBookmarks 是按 docx 里**实际存在的书签**
            //          匹配字典的，模板没埋该签名时，空值不会写入任何内容。
            // 保守边界：TemplateStructure 为 null（历史脏数据）时按「需要」处理，继续走 TC005，
            //          不静默跳过 —— 结构都读不到的模板宁可报错。
            var structure = template.TemplateStructure;
            if (structure != null && structure.TestConditionCount <= 0)
            {
                return string.Empty;
            }

            // ★ 关键：文本模板匹配必须使用 pool.Conditions（原始条件），
            //   不能用运行时条件字典（后者多了 TestItemId / TestMethod 以及参数集键）。
            //   这与 datasheet 侧历史实现一致，改动会导致文本模板匹配口径漂移。
            var textTemplate = template.FindTextTemplate(pool.Conditions);
            if (textTemplate == null)
            {
                throw new TestConditionCompositionException(
                    TestConditionErrorCodes.TemplateTextTemplateMissing,
                    $"模板 {template.Id} 缺少与当前条件对应的 testcondition 文本模板",
                    detail: "请检查模板中 TestConditionTextTemplate 的条件键是否与 ConditionPool.Conditions 对齐。");
            }

            var paramJson = paramSet?.Values != null
                ? JsonSerializer.Serialize(paramSet.Values, JsonOptions)
                : "{}";

            return TextReplaceHelper.FillTemplate(textTemplate.Text, paramJson);
        }

        /// <inheritdoc />
        public async Task<TestConditionComposition> ComposeForPackageAsync(
            CheckListItem item,
            ConditionPool pool,
            TestPointPackage package,
            CancellationToken ct)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var runtimeConditions = await BuildRuntimeConditionsAsync(pool, package.ParamSet, item, ct);
            var template = ResolveTemplate(runtimeConditions);
            var text = ComposeText(template, pool, package.ParamSet);

            return new TestConditionComposition(text, template, runtimeConditions);
        }

        /// <inheritdoc />
        public async Task<TestConditionText> ComposeForItemAsync(
            CheckListItem item,
            ConditionPool pool,
            CancellationToken ct)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            // 分包复用领域服务：零仓储依赖，纯内存计算，与 datasheet 侧同一份实现。
            var packages = _domainService.PackByParamSet(item);
            if (packages.Count == 0)
            {
                throw new TestConditionCompositionException(
                    TestConditionErrorCodes.TestPointParamsMissing,
                    $"检测项 {item.TestItemId} 未生成任何参数分组（packages 为空）",
                    testPoint: item.TestItemId?.Value,
                    detail: "请先执行参数计算（CalculateParamAsync），使 TestPointParams 落库后再生成 checklist。");
            }

            var lines = new List<string>(packages.Count);
            foreach (var package in packages)
            {
                // TODO(封装) 分属不同 package 但条件相同时会重复调用 FindByIndex（每次可能查库）。
                //            功能优先暂不做模板缓存，见本目录 README「未来优化方向」。
                var composition = await ComposeForPackageAsync(item, pool, package, ct);

                // 无参数的测点组整段跳过：只留标签不填内容会让单元格出现孤立的 "[A]:" 空行，
                // 比留白更难看，也容易让人误以为该测点漏算了参数。
                if (string.IsNullOrWhiteSpace(composition.Text)) continue;

                // 每段前带「测点组」标签（如 [A]: 或 [B/C-body/C-bottom]:），
                // 让纸质清单能看出该段条件对应哪些测点——这是「即读即用」的关键信息，
                // 缺了它读者无法判断两段条件分别属于哪个样品。
                // package.TestPointIds 已由领域服务 PackByParamSet 排序，此处不再重复排序。
                var label = $"[{string.Join("/", package.TestPointIds)}]:";
                lines.Add($"{label}\r\n{composition.Text}");
            }

            // 组内去重：标签 + 文本整体相同才合并（业务口径：相同值只写首行）。
            // 之所以把标签纳入去重键，是为了对齐既有样本形态——
            // 测点组合与条件都相同才视为同一组，否则保留为独立段。
            var deduped = lines.Distinct(StringComparer.Ordinal).ToList();

            // 段与段之间留一个空行（\r\n\r\n），让多个测点组在单元格里视觉分开、更易读；
            // 整格末尾不额外留空行——那只会撑高行高，并不增加可读性。
            // 若所有测点组都没参数，deduped 为空，此处返回 string.Empty，
            // 由 CheckListAdapter 写成一个空白单元格（即「没有任何参数的地方置空」）。
            return new TestConditionText(string.Join("\r\n\r\n", deduped), deduped, packages.Count);
        }

        /// <summary>
        /// 查询检测项关联的标准实体。
        ///
        /// 只供 <see cref="BuildRuntimeConditionsAsync"/> 调用一次，
        /// 其结果的**两处消费**分别是 <see cref="BuildTestMethodString"/>（拼展示串）
        /// 与 StandardMethod 解析，避免二次查库。
        /// </summary>
        /// <param name="item">检测项，提供 StandardIds。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>标准实体列表；查不到时返回空列表（不抛异常）。</returns>
        private async Task<IReadOnlyList<Standard>> LoadStandardsAsync(CheckListItem item, CancellationToken ct)
        {
            var standardIds = item.StandardIds
                .Where(id => id != null)
                .Select(id => new StandardId(id!.Value))
                .ToList();

            var standards = await _standardRepository.GetByIdsAsync(standardIds, ct);
            return standards?.ToList() ?? new List<Standard>();
        }

        /// <summary>
        /// 拼接 TestMethod 展示串（纯函数，不查库）。
        /// </summary>
        /// <param name="item">检测项，提供降级用的 StandardIds 原值。</param>
        /// <param name="standards">已查出的标准实体。</param>
        /// <returns>以 ";" 拼接的标准编号串；查不到标准实体时降级为 StandardIds 原值。</returns>
        private static string BuildTestMethodString(CheckListItem item, IReadOnlyList<Standard> standards)
        {
            // 排序是刚性要求：TestMethod 会进入模板索引的 MatchesAll 逐字比对，
            // 若依赖数据库返回顺序，同一组标准可能拼出 "A;B" 或 "B;A"，造成索引匹配漂移。
            return standards.Count > 0
                ? string.Join(";", standards
                    .OrderBy(s => s.Id.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(s => s.StandardCode ?? s.StandardCodeNameEn ?? s.Id.Value))
                : string.Join(";", item.StandardIds.Select(id => id?.Value ?? string.Empty));
        }
    }
}
