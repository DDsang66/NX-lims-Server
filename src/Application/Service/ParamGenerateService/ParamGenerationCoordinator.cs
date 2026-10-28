using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.FormulaContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.FormulaContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ParamStructureContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ParamStructureContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository.ParamEngineContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.Engine;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.Engine.Condition;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Util;
using NX_lims_Softlines_Command_System.src.Domain.Services.Compensation;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Service;
using System.Text.Json;
using System.Threading.Tasks;

namespace NX_lims_Softlines_Command_System.src.Application.Service.ParamGenerateService
{
    /// <summary>
    /// 应用层协调器：负责跨聚合加载、调用富化器/引擎/补偿服务、并返回最终 ParamSet
    /// - 不直接持久化（调用方可在事务边界内保存）
    /// </summary>
    public class ParamGenerationCoordinator:IScopedDependency
    {
        private readonly IParamStructureRepository _structureRepo;
        private readonly IFormulaRepository _formulaRepo;
        private readonly IParamRuleRepository _ruleRepo;
        private readonly ITestItemRepository _testItemRepository;
        private readonly IParamGenerationEngine _engine;
        private readonly IParamCompensationService _compensation;
        private readonly IConditionPoolValidateService _conditionPoolValidateService;
        private readonly IParamValidateService _paramValidateService;

        public ParamGenerationCoordinator(
            IParamStructureRepository structureRepo,
            IFormulaRepository formulaRepo,
            IParamRuleRepository ruleRepo,
            ITestItemRepository testItemRepository,
            IParamGenerationEngine engine,
            IParamCompensationService compensation,
            IConditionPoolValidateService conditionPoolValidateService,
            IParamValidateService paramValidateService)
        {
            _structureRepo = structureRepo;
            _conditionPoolValidateService = conditionPoolValidateService;
            _formulaRepo = formulaRepo;
            _testItemRepository = testItemRepository;
            _ruleRepo = ruleRepo;
            _engine = engine;
            _compensation = compensation;
            _paramValidateService = paramValidateService;
        }

        /// <summary>
        /// 生成参数用例排列
        /// </summary>
        /// <param name="structure"></param>
        /// <param name="pool"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<ParamGenerateOutput>> GenerateAsync(
            ParamStructure structure,
            ConditionPool pool,
            CancellationToken ct)
        {
            // 加载 Formula（可选）
            Formula? formula = null;
            if (structure.FormulaId != null)
            {
                 formula = await  _formulaRepo.GetByIdAsync(structure.FormulaId, ct);
            }

            // 2. 验证 ConditionPool
            var validation = await _conditionPoolValidateService.ValidateConditionPool(structure, formula, pool);

            if (validation.IsFailure)
                return Result<ParamGenerateOutput>.Fail(validation.Error);

            // 3. 加载规则

            var rules = await _ruleRepo.GetByStructureIdAsync(structure.Id, ct);

            // 4. 引擎生成
            var generated = _engine.Generate(pool, rules);

            //待做：检查当前参数对应的structure的中的字段，是否可以作为Pool中的条件存在，如果是
            //则将当前的参数字段名作为key注入到pool中，value为当前参数的值，作为后续参数生成的条件之一

            // 5. 验证 + 补偿
            var main = structure.MainParamDefinition;
            var isValid = _paramValidateService.Validate(generated, structure);

            generated.TryGetValue(main.Name, out var value);

            var typedValue = ConvertToTypedValueService.Convert(
                value,
                structure.MainParamDefinition.ValueTypeName
                );

            var typedDefault = ConvertToTypedValueService.Convert(
                main.DefaultValue,
                structure.MainParamDefinition.ValueTypeName
            );

            if (isValid)
                _compensation.CompensateParamWithStructure(generated, main.Name, typedValue, typedDefault);
            else
                _compensation.CompensateParamWithStructure(generated, main.Name, null, typedDefault);

            // === 核心改变：实现你的“待做”逻辑，但不修改 pool，而是构建新的条件池 ===
            var newConditions = new Dictionary<string,object>();
                                                     // 伪代码：检查当前 structure 的字段，将生成的参数转为条件
            foreach (var param in generated.Values)
            {
                if (structure.IsEligibleAsCondition) // 你的业务判断逻辑
                {
                    newConditions.Add(param.Key, param.Value!);
                }
            }

            var output = new ParamGenerateOutput(generated, newConditions);

            return Result<ParamGenerateOutput>.Ok(output);
        }

        /// <summary>
        /// 补偿服务：与TestItem定义的Param比较，计算差异值生成最终BasicParamSet
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<ParamSet>> FinalGenerateAsync(TestItemId itemId, ParamSet param, CancellationToken ct) 
        {
            var testItem = await _testItemRepository.GetByIdAsync(itemId, ct);

            if (testItem == null)
                return Result<ParamSet>.Fail("TestItem not found");

            var definitions = testItem.ParamRequireDefinitions;

            // 2. 领域逻辑：参数补偿（确保结构完整，缺失值补默认值）
            // 将具体的 foreach 和赋值逻辑封装到领域服务中
            _compensation.CompensateWithItemDefinitions(param, definitions);

            // 3. 领域逻辑：参数验证（验证类型是否正确、是否符合业务规则，绝不修改 Param）
            // 如果验证失败，返回包含错误信息的 Result
            var validationResult = _paramValidateService.ValidateWithItemDefinitions(param, definitions);

            if (!validationResult)
            {
                return Result<ParamSet>.Fail("参数验证失败：缺失、为空或类型不匹配。");
            }

            return Result<ParamSet>.Ok(param);
        }
    }
}
