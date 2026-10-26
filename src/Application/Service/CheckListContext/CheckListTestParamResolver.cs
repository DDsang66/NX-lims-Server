using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.CheckListContext;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext;
using NX_lims_Softlines_Command_System.src.Application.Interface;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository.ParamEngineContext;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Application.Service.CheckListContext
{
    /// <summary>
    /// checklist 侧 testParam 补齐服务的实现。
    ///
    /// 职责链：查条件池 → 为每个检测项定位命中的池 → 调
    /// <see cref="ITestConditionComposer.ComposeForItemAsync"/> 算 testcondition 文本
    /// → 定位对应的 dto 条目 → 覆盖 Parameter → 组内去重后覆盖 CuttingMethod。
    ///
    /// 本类不注入 <c>ILogger</c>：异常一律向上抛，由调用方
    /// （<c>CheckListAppService.GenerateCheckListAsync</c>）统一记录结构化日志，
    /// 避免同一失败被记录两次。
    /// </summary>
    public class CheckListTestParamResolver : IScopedDependency, ICheckListTestParamResolver
    {
        /// <summary>
        /// 未查询 TestItem 名称字典时的哨兵值：
        /// 说明「全部条目都按 ID 精确命中，根本不需要查名称」。
        /// </summary>
        private static readonly Dictionary<string, TestItem> NoNameLookup = new(StringComparer.Ordinal);

        /// <summary>条件池仓储，用于按 checklist 取出全部条件池。</summary>
        private readonly IConditionPoolRepository _conditionPoolRepository;

        /// <summary>检测项仓储，仅在按 ID 无法命中 dto 条目时用于取 NameEn / NameChn 做名称匹配。</summary>
        private readonly ITestItemRepository _testItemRepository;

        /// <summary>testcondition 文本合成器（与 datasheet 侧共用同一实现）。</summary>
        private readonly ITestConditionComposer _composer;

        /// <summary>
        /// 构造 checklist 侧 testParam 补齐服务。
        /// </summary>
        /// <param name="conditionPoolRepository">条件池仓储。</param>
        /// <param name="testItemRepository">检测项仓储（名称匹配用）。</param>
        /// <param name="composer">testcondition 合成器。</param>
        /// <exception cref="ArgumentNullException">任一依赖为 null 时抛出。</exception>
        public CheckListTestParamResolver(
            IConditionPoolRepository conditionPoolRepository,
            ITestItemRepository testItemRepository,
            ITestConditionComposer composer)
        {
            _conditionPoolRepository = conditionPoolRepository ?? throw new ArgumentNullException(nameof(conditionPoolRepository));
            _testItemRepository = testItemRepository ?? throw new ArgumentNullException(nameof(testItemRepository));
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        }

        /// <inheritdoc />
        public async Task ApplyAsync(CheckList checkList, CheckListGenerateDto dto, CancellationToken ct)
        {
            if (checkList == null) throw new ArgumentNullException(nameof(checkList));
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var checkListId = checkList.Id.Value;

            // 1. 时序校验（显式）：条件池必须齐备，缺失即阻断。
            //    这是「checklist 生成依赖条件齐备」这条隐性约定的代码化落点。
            var pools = (await _conditionPoolRepository.GetByCheckListIdAsync(checkList.Id, ct))?.ToList()
                        ?? new List<ConditionPool>();

            if (pools.Count == 0)
            {
                throw new TestConditionCompositionException(
                    TestConditionErrorCodes.ConditionPoolMissing,
                    $"CheckList {checkListId} 尚未生成条件池，无法生成 testParam",
                    checkListId: checkListId,
                    detail: "请先完成参数计算（CalculateParamAsync），使 ConditionPool 落库后再生成 checklist。");
            }

            // 2. 条目数量校验：数量不一致说明前端增删了行，此时任何键匹配都可能错位。
            var dtoItems = dto.Items?.ToList() ?? new List<CheckListResponseItemDto>();
            if (dtoItems.Count != checkList.Items.Count)
            {
                throw new TestConditionCompositionException(
                    TestConditionErrorCodes.DtoItemNotAligned,
                    $"dto 条目数({dtoItems.Count})与 checklist 聚合条目数({checkList.Items.Count})不一致，无法对齐 testParam。",
                    checkListId: checkListId,
                    detail: "请确认前端未增删 checklist 行。");
            }

            // 3. 名称字典按需加载：
            //    若全部条目都能按 TestItemId 精确命中（前端原样透传 Get 结果的场景），
            //    则完全不查 TestItem 表，保持零额外开销。
            var testItemMap = await LoadTestItemMapIfNeededAsync(checkList, dtoItems, ct);

            // 4. 逐项计算并回写
            foreach (var item in checkList.Items)
            {
                var pool = ResolvePoolForItem(pools, item, checkListId);

                // 分包 + 逐包合成 + 组内去重，全部在公共组件内完成
                var conditionText = await _composer.ComposeForItemAsync(item, pool, ct);

                var target = ResolveDtoItem(dtoItems, item, testItemMap, checkListId);

                // 仅覆盖这两列；其余字段保持前端传入值不动（前端契约零变更）
                target.Parameter = conditionText.Text;
                target.CuttingMethod = DedupCuttingMethod(target.CuttingMethod);
            }
        }

        /// <summary>
        /// 按需加载 TestItem 名称字典。
        ///
        /// 只有存在「按 TestItemId 无法精确命中 dto 条目」的检测项时才查库，
        /// 且**只查一次**（<see cref="ITestItemRepository"/> 未提供按 ID 批量查询的方法，
        /// 故取全量后在内存建索引；该操作仅在 checklist 生成这一低频人工路径上触发）。
        /// </summary>
        /// <param name="checkList">checklist 聚合。</param>
        /// <param name="dtoItems">前端传入的 dto 条目。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>TestItemId → TestItem 的字典；若无需名称匹配则返回空字典。</returns>
        private async Task<Dictionary<string, TestItem>> LoadTestItemMapIfNeededAsync(
            CheckList checkList,
            IReadOnlyList<CheckListResponseItemDto> dtoItems,
            CancellationToken ct)
        {
            var dtoTestItemValues = dtoItems
                .Select(d => d.TestItem)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToHashSet(StringComparer.Ordinal);

            bool allMatchedById = checkList.Items.All(i =>
                i.TestItemId != null && dtoTestItemValues.Contains(i.TestItemId.Value));

            if (allMatchedById) return NoNameLookup;

            var allTestItems = await _testItemRepository.GetAllTestItemsAsync(ct) ?? Enumerable.Empty<TestItem>();

            return allTestItems
                .Where(t => t?.Id != null)
                .ToDictionary(t => t.Id.Value, t => t, StringComparer.Ordinal);
        }

        /// <summary>
        /// 为检测项定位对应的 dto 条目。
        ///
        /// 匹配分三级（**不使用位置下标兜底**——错位写入比直接失败更难排查）：
        /// 1. `dto.TestItem` 精确等于 TestItemId（前端原样透传 Get 结果的场景）；
        /// 2. `dto.TestItem` 包含 TestItem.NameEn / NameChn（前端拼成 "NameEn(NameChn)" 展示的场景），
        ///    或反向包含（前端截断了长名字）；
        /// 3. 用测点集合（Samples）消歧——注意同一 checklist 内多个检测项测点完全相同的情况很常见，
        ///    因此 Samples 只能缩小候选，不能单独作为主键。
        /// </summary>
        /// <param name="dtoItems">前端传入的 dto 条目。</param>
        /// <param name="item">当前处理的检测项。</param>
        /// <param name="testItemMap">TestItemId → TestItem 字典；空字典表示未查询。</param>
        /// <param name="checkListId">所属 checklist 标识，用于异常上下文。</param>
        /// <returns>唯一确定的 dto 条目。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 错误码 TC007：无法唯一确定对应条目（0 个或仍多于 1 个候选）。
        /// </exception>
        private static CheckListResponseItemDto ResolveDtoItem(
            IReadOnlyList<CheckListResponseItemDto> dtoItems,
            CheckListItem item,
            Dictionary<string, TestItem> testItemMap,
            Guid checkListId)
        {
            var testItemId = item.TestItemId?.Value;

            // ---- 第 1 级：按 TestItemId 精确匹配（零成本）----
            if (!string.IsNullOrEmpty(testItemId))
            {
                var exact = dtoItems.FirstOrDefault(d =>
                    string.Equals(d.TestItem, testItemId, StringComparison.Ordinal));

                if (exact != null) return exact;
            }

            // ---- 第 2 级：按 TestItem 名称匹配 ----
            var candidates = new List<CheckListResponseItemDto>();

            if (testItemMap.Count > 0
                && !string.IsNullOrEmpty(testItemId)
                && testItemMap.TryGetValue(testItemId, out var testItem))
            {
                var nameEn = testItem.NameEn;
                var nameChn = testItem.NameChn;

                // 名字为空时不能参与 Contains 判断：Contains("") 恒为 true，会污染候选集
                bool hasNameEn = !string.IsNullOrWhiteSpace(nameEn);
                bool hasNameChn = !string.IsNullOrWhiteSpace(nameChn);

                if (hasNameEn || hasNameChn)
                {
                    candidates = dtoItems.Where(d =>
                    {
                        var dtoName = d.TestItem;
                        if (string.IsNullOrWhiteSpace(dtoName)) return false;

                        if (hasNameEn && dtoName.Contains(nameEn, StringComparison.OrdinalIgnoreCase)) return true;
                        if (hasNameChn && dtoName.Contains(nameChn, StringComparison.OrdinalIgnoreCase)) return true;

                        // 反向包含：应对前端把长名字截断的情况
                        if (hasNameEn && nameEn.Contains(dtoName, StringComparison.OrdinalIgnoreCase)) return true;

                        return false;
                    }).ToList();

                    if (candidates.Count == 1) return candidates[0];
                }
            }

            // ---- 第 3 级：用测点集合消歧 ----
            var disambiguationPool = candidates.Count > 0 ? candidates : dtoItems.ToList();
            var expectedSamples = item.Samples?.ToHashSet(StringComparer.Ordinal)
                                  ?? new HashSet<string>(StringComparer.Ordinal);

            if (expectedSamples.Count > 0)
            {
                var narrowed = disambiguationPool
                    .Where(d => d.Samples != null
                                && d.Samples.Count == expectedSamples.Count
                                && expectedSamples.SetEquals(d.Samples))
                    .ToList();

                if (narrowed.Count == 1) return narrowed[0];
            }

            // ---- 兜底：明确报错，并输出足够运维自愈的上下文 ----
            var nameEnForMessage = testItemMap.Count > 0
                                   && !string.IsNullOrEmpty(testItemId)
                                   && testItemMap.TryGetValue(testItemId, out var itemForMessage)
                ? itemForMessage.NameEn
                : "<未查询或库中不存在>";

            throw new TestConditionCompositionException(
                TestConditionErrorCodes.DtoItemNotAligned,
                $"dto 无法与清单条目唯一对齐。CheckListId={checkListId}，" +
                $"TestItemId={testItemId}，NameEn=\"{nameEnForMessage}\"，" +
                $"Samples=[{string.Join(",", item.Samples ?? new List<string>())}]，名称匹配候选数={candidates.Count}",
                checkListId: checkListId,
                testPoint: testItemId,
                detail: "dto 实际条目：" + string.Join(" | ", dtoItems.Select(d =>
                    $"TestItem=\"{d.TestItem}\",Samples=[{string.Join(",", d.Samples ?? new List<string>())}]")));
        }

        /// <summary>
        /// 为检测项定位命中的条件池。
        ///
        /// 口径与原 <c>CheckListAppService.CalculateParamAsync</c> 一致：
        /// 按测点逐个用 <c>ConditionPool.TestPoints.Contains(testPoint)</c> 匹配，取首个命中。
        /// </summary>
        /// <param name="pools">该 checklist 下的全部条件池。</param>
        /// <param name="item">检测项，提供 Samples 测点集合。</param>
        /// <param name="checkListId">所属 checklist 标识，用于异常上下文。</param>
        /// <returns>命中的条件池。</returns>
        /// <exception cref="TestConditionCompositionException">
        /// 错误码 TC002：该检测项的所有测点均未命中任何条件池。
        /// </exception>
        private static ConditionPool ResolvePoolForItem(
            IReadOnlyList<ConditionPool> pools,
            CheckListItem item,
            Guid checkListId)
        {
            if (item.Samples != null)
            {
                foreach (var testPoint in item.Samples)
                {
                    var matched = pools.FirstOrDefault(p =>
                        p.TestPoints != null && p.TestPoints.Contains(testPoint));

                    if (matched != null) return matched;
                }
            }

            // 退化处理：无测点信息但整个 checklist 只有一个池时，可无歧义地使用它
            if (pools.Count == 1) return pools[0];

            throw new TestConditionCompositionException(
                TestConditionErrorCodes.ConditionPoolNotMatchedForTestPoint,
                $"检测项 {item.TestItemId} 的测点在条件池中均未命中",
                checkListId: checkListId,
                testPoint: item.TestItemId?.Value,
                detail: "请检查 ConditionPool.TestPoints 是否覆盖该检测项的样品测点。");
        }

        /// <summary>
        /// CuttingMethod 组内去重：按行、按分号拆分后去掉完全相同的片段，保留首次出现。
        ///
        /// 该列实际有两种可能的分隔形态：<c>ProcessParameterCutting</c> 用
        /// <c>string.Join("; ", parts)</c> 产出的「分号分隔」形态（本项目当前就是这种，
        /// 同一次检测的多个测点各贡献一段），以及换行分隔的多行文本。
        /// 只按换行去重会让 "A; A; A" 这种数据原样保留，因此两种分隔都要拆。
        ///
        /// 业务口径（用户确认）：相同值只写首行。对单值输入天然幂等。
        /// </summary>
        /// <param name="cuttingMethod">
        /// 前端传入的取样方法文本，可含换行、或用 <c>"; "</c> 分隔多段。
        /// </param>
        /// <returns>去重后的文本；输入为空时返回空字符串。</returns>
        private static string DedupCuttingMethod(string? cuttingMethod)
        {
            if (string.IsNullOrWhiteSpace(cuttingMethod)) return string.Empty;

            // 保持原有呈现形态：原文含换行则换行回拼，否则用 "; " 回拼，
            // 避免把本来就单行的一格强行拆成多行。
            bool hasLineBreak = cuttingMethod.Contains('\n') || cuttingMethod.Contains('\v');

            var segments = new List<string>();
            foreach (var line in cuttingMethod.Split(new[] { "\r\n", "\n", "\v" }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var segment in line.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = segment.Trim();
                    if (trimmed.Length > 0) segments.Add(trimmed);
                }
            }

            var deduped = segments.Distinct(StringComparer.Ordinal);

            return string.Join(hasLineBreak ? "\r\n" : "; ", deduped);
        }
    }
}
