using Mapster;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TestItemContext;
using NX_lims_Softlines_Command_System.src.Application.Interface.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Application.Service.TestItemContext
{
    public class TestItemQueryService:ITestItemQueryService,IScopedDependency
    {
        private readonly ITestItemRepository _testItemRepository;
        public TestItemQueryService(ITestItemRepository testItemRepository) 
        {
            _testItemRepository = testItemRepository;
        }
        /// <summary>
        /// 根据id获取TestItem
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<TestItemResponseDto>> GetTestItemByIdAsync(string id, CancellationToken ct)
        {
            var testItem = await _testItemRepository.GetByIdAsync(new TestItemId(id), ct);

            var testItemDto = testItem.Adapt<TestItemResponseDto>();

            return Result<TestItemResponseDto>.Ok(testItemDto);
        }

        /// <summary>
        /// 获取TestItem列表
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<List<TestItemResponseDto>>> GetTestItemsAsync(CancellationToken ct)
        {
            var items = await  _testItemRepository.GetAllTestItemsAsync(ct);

            var itemDtos = items.Adapt<List<TestItemResponseDto>>();

            return Result<List<TestItemResponseDto>>.Ok(itemDtos);
        }
    }
}
