using NX_lims_Softlines_Command_System.src.Application.Interface;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.BuyerContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.FormulaContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ParamRuleContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ParamStructureContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ParamStructureContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.Standard.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.Enums;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository.ParamEngineContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.Engine;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Domain.Share.Interface;
using NX_lims_Softlines_Command_System.src.Infrastructure.Data.Repository;
using System.Runtime.CompilerServices;

namespace NX_lims_Softlines_Command_System.src.Application.Service.ParamGenerateService
{
    public class ParamGenerationUseCaseService :IParamGenerationUseCaseService, IScopedDependency
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITestItemRepository _testItemRepository;
        private readonly IParamStructureRepository _structureRepo;
        private readonly IFormulaRepository _formulaRepo;
        private readonly IParamRuleRepository _ruleRepo;
        private readonly IStandardFamilyRepository _familyRepo;
        private readonly IConditionPoolRepository _conditionPoolRepo;
        private readonly IParamEngineScheduler _paramEngineScheduler;
        private readonly ParamGenerationCoordinator _coordinator;

        public ParamGenerationUseCaseService(
            IUnitOfWork unitOfWork,
            IParamStructureRepository structureRepo,
            IFormulaRepository formulaRepo,
            IParamRuleRepository ruleRepo,
            IStandardFamilyRepository familyRepo,
            IConditionPoolRepository conditionPoolRepo,
            ITestItemRepository testItemRepository,
            IParamEngineScheduler paramEngineScheduler,
            ParamGenerationCoordinator coordinator)
        {
            _unitOfWork = unitOfWork;
            _structureRepo = structureRepo;
            _formulaRepo = formulaRepo;
            _ruleRepo = ruleRepo;
            _familyRepo = familyRepo;
            _testItemRepository = testItemRepository;
            _conditionPoolRepo = conditionPoolRepo;
            _paramEngineScheduler = paramEngineScheduler;
            _coordinator = coordinator;
        }



        //Note:未来升级到多线程平行生成时，需考虑 CheckListItem 的标准顺序与依赖关系，避免并发冲突。

        /// <summary>
        /// 为 CheckList 的某个 Item 生成参数
        /// 串行工作流，买家层覆盖
        /// 先执行与买家相关的 formula/structure，再用标准层补齐缺项
        /// </summary>
        /// <param name="checkListItemId"></param>
        /// <param name="pool"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<Result<ParamSet>> GenerateForCheckListItemAsync(
            CheckListItem checkListItem,
            ConditionPool pool,
            CancellationToken ct)
        {
            var paramSet = new ParamSet();

            var testItem = await _testItemRepository.GetByIdAsync(checkListItem.TestItemId!, ct);
            if (testItem == null) return Result<ParamSet>.Fail("TestItem not found");

            // testItem 给出需要的参数集合（用所有定义的 ParamName）
            var requiredParamNames = testItem.ParamRequireDefinitions
                .Select(p => p.ParamName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 从 pool 中尝试读取 buyer 标识与散客标志（前端已包含这些信息的假设）
            string? buyerCode = null;
            bool isIndividualTraveler = false;
            if (pool.HasCondition("BuyerCode"))
            {
                try { buyerCode = pool.GetConditionValue<string>("BuyerCode"); } catch { buyerCode = null; }
            }
            if (pool.HasCondition("BuyerIsIndividualTraveler"))
            {
                try { isIndividualTraveler = pool.GetConditionValue<bool>("BuyerIsIndividualTraveler"); } catch { isIndividualTraveler = false; }
            }

            // 遍历每个标准，先执行与买家相关的 formula/structure，再用标准层补齐缺项
            foreach (var standardId in checkListItem.StandardIds)
            {
                // 找到该标准下的标准族
                var family = await _familyRepo.GetByStandardIdAsync(standardId, ct);
                if (family == null) 
                    throw new Exception("未找到标准所属的 Family");

                // 收集该 family 下的结构与公式（由调度器统一收集规则/公式/结构）
                var schedule = await _paramEngineScheduler.CollectForTestItemAsync(testItem.Id, new[] { standardId }, ct);

                var structures = await _structureRepo.GetByFamilyIdAsync(family.Id, ct);

                var sortedParamStructures = structures
                    .OrderByDescending(ps => ps.IsEligibleAsCondition)
                    .ToList();

                // 1) 买家层优先：如果存在 buyer 且不是散客，则先执行 buyer 关联的公式对应的结构
                var buyerFormulaIds = new HashSet<FormulaId?>();
                if (!string.IsNullOrWhiteSpace(buyerCode)
                    && !isIndividualTraveler 
                    && schedule.Formulas != null)
                {
                    foreach (var f in schedule.Formulas)
                    {
                        if (f == null) continue;

                        if (f.BuyerIds != null && f.BuyerIds.Any(b => b != null && string.Equals(b.Value, buyerCode, StringComparison.OrdinalIgnoreCase)))
                        {
                            buyerFormulaIds.Add(f.Id);
                        }
                    }
                }

                // 执行结构中属于 buyerFormulaIds 的结构，优先写入 paramSet
                if (buyerFormulaIds.Any())
                {
                    foreach (var structure in sortedParamStructures.Where(s => s.FormulaId != null && buyerFormulaIds.Contains(s.FormulaId)))
                    {
                        var r = await _coordinator.GenerateAsync(structure, pool, ct);
                        if (r.IsSuccess)
                        {
                            paramSet.Merge(r.Value!.ParamSet!);
                            //   paramSet.Merge(r.Value.ParamSet);

                            // 串行执行，前一个的结果立刻成为后一个的条件，天然有序，绝对安全
                            pool.Merge(r.Value.NewCondition);
                        }
                    }
                }

                // 2) 对比 testItem 的需求参数，若仍有缺失则用标准层结构补齐
                var missingParams = requiredParamNames.Where(p => !paramSet.Contains(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (missingParams.Any())
                {
                    // 只对能生成缺失参数的结构执行标准层生成：按结构的 ParamName 与缺失集合匹配
                    foreach (var structure in sortedParamStructures.Where(s => s.FormulaId == null || !buyerFormulaIds.Contains(s.FormulaId)))
                    {
                        if (!missingParams.Contains(structure.ParamName)) continue;

                        var r = await _coordinator.GenerateAsync(structure, pool, ct);
                        if (r.IsSuccess)
                        {
                            paramSet.Merge(r.Value!.ParamSet!);

                            //   paramSet.Merge(r.Value.ParamSet);

                            // 串行执行，前一个的结果立刻成为后一个的条件，天然有序，绝对安全
                            pool.Merge(r.Value.NewCondition);

                            // 更新缺失集合，若已补齐则可提前跳出
                            missingParams = requiredParamNames.Where(p => !paramSet.Contains(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            if (!missingParams.Any()) break;
                        }
                    }
                }
            }

            // 最终补偿与校验：与 TestItem 定义对比并返回最终 ParamSet
            var finalParamSet = await _coordinator.FinalGenerateAsync(checkListItem.TestItemId!, paramSet, ct);

            return finalParamSet.IsSuccess 
                ? Result<ParamSet>.Ok(finalParamSet.Value!) 
                : Result<ParamSet>.Fail(finalParamSet.Error);
        }

        /// <summary>
        /// 为某个 ParamStructure 生成参数
        /// </summary>
        /// <param name="structureId"></param>
        /// <param name="pool"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<ParamSet>> GenerateForStructureAsync(
            ParamStructureId structureId,
            ConditionPool pool,
            CancellationToken ct)
        {
            var structure = await _structureRepo.GetByIdAsync(structureId, ct);
            if (structure == null) return Result<ParamSet>.Fail("ParamStructure not found");

            var result = await _coordinator.GenerateAsync(structure, pool, ct);

            return result.Value!.ParamSet;
        }
    }
}
