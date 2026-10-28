using Mapster;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TestItemContext;
using NX_lims_Softlines_Command_System.src.Application.Interface.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.Enums;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Domain.Share.Enums;
using NX_lims_Softlines_Command_System.src.Domain.Share.Interface;

namespace NX_lims_Softlines_Command_System.src.Application.Service.TestItemContext
{
    public class TestItemAppService:IScopedDependency, ITestItemAppService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITestItemRepository _testItemRepository;

        public TestItemAppService(IUnitOfWork unitOfWork,ITestItemRepository testItemRepository) 
        {
            this._unitOfWork = unitOfWork;
            _testItemRepository = testItemRepository;
        }

        /// <summary>
        /// 新建测试项目
        /// </summary>
        /// <returns></returns>
        public async Task<Result> AddTestItemAsync(AddTestItemDto dto,CancellationToken ct) 
        {
            var testItem = dto.Adapt<TestItem>();

            await _testItemRepository.AddAsync(testItem,ct);

            await _unitOfWork.SaveChangesAsync();

            return Result.Ok();
        }

        /// <summary>
        /// Update TestItem
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result> UpdateTestItemAsync(UpdateTestItemDto dto,CancellationToken ct) 
        {
            var testItem = await _testItemRepository.GetByIdAsync(new TestItemId(dto.TestItemId), ct);

            var paramDefs = dto.ParamRequireDefinitions?
                .Select(p =>
                {
                    var def = ParamRequireDefinition.Create(p.ParamName, p.ParamTypeName, p.UniversalDefault, p.IsRequired);
                    
                    if (p.StandardDefaults != null)
                    {
                        foreach (var kv in p.StandardDefaults)
                        {
                            // 尝试将 key 解析为 StandardType 枚举（忽略大小写）
                            if (Enum.TryParse<StandardType>(kv.Key, true, out var st))
                            {
                                def = def.WithStandardDefault(st, kv.Value);     
                            }          
                        }
                    }
                    return def;
                }).ToList();

            // 2. 安全转换 Group（忽略大小写）
            if (!Enum.TryParse<TestGroup>(dto.Group, ignoreCase: true, out var groupEnum))
            {
                return Result.Fail($"Invalid group value: '{dto.Group}'. Valid values are: {string.Join(", ", Enum.GetNames<TestGroup>())}");
            }

            // 3. 安全转换 Status（忽略大小写）
            if (!Enum.TryParse<Status>(dto.Status, ignoreCase: true, out var statusEnum))
            {
                return Result.Fail($"Invalid status value: '{dto.Status}'. Valid values are: {string.Join(", ", Enum.GetNames<Status>())}");
            }

            testItem.Update(nameEN: dto.TestItemNameEn, nameChn: dto.TestItemNameChn,description: dto.Description, isFeasible: dto.IsFeasible,group: groupEnum,status: statusEnum,paramRequireDefinitions: paramDefs);

            await _testItemRepository.UpdateAsync(testItem, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Ok();
        }

    }
}
