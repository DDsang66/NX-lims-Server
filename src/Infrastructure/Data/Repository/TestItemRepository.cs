using Mapster;
using Microsoft.EntityFrameworkCore;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Data.Persistence;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.Data.Repository
{
    public class TestItemRepository: ITestItemRepository, IScopedDependency
    {
        private readonly dbContext _dbContext;

        public TestItemRepository(dbContext dbContext) 
        {
            _dbContext = dbContext;
        }
        /// <summary>
        /// 添加聚合根
        /// </summary>
        /// <param name="aggregateRoot"></param>
        /// <returns></returns>
        public async Task AddAsync(TestItem aggregateRoot, CancellationToken ct) { }

        /// <summary>
        /// 修改聚合根
        /// </summary>
        /// <param name="aggregateRoot"></param>
        /// <returns></returns>
        public async Task UpdateAsync(TestItem aggregateRoot, CancellationToken ct)
        {
            var testItemPo = aggregateRoot.Adapt<BasicItem>();
            // 1. 先在当前 DbContext 的本地跟踪图中查找同主键的旧实体
            var localEntity = _dbContext.Set<BasicItem>().Local.FirstOrDefault(e => e.IdItem == testItemPo.IdItem);

            // 2. 如果旧实体正在被跟踪，则将其从跟踪图中剥离
            if (localEntity != null)
            {
                _dbContext.Entry(localEntity).State = EntityState.Detached;
            }

            // 3. 此时跟踪图中已无冲突，安全地附加新实体并标记为修改状态
            _dbContext.Entry(testItemPo).State = EntityState.Modified;
        }

        /// <summary>
        /// 查询聚合根
        /// </summary>
        /// <param name="aggregateRootId"></param>
        /// <param name="ct"></param>
        /// <returns>聚合根</returns>
        public async Task<TestItem> GetByIdAsync(TestItemId aggregateRootId, CancellationToken ct) 
        {
            var testItemPo = await  _dbContext.FindAsync<BasicItem>(aggregateRootId.Value, ct);
           
            var testItem = testItemPo.Adapt<TestItem>();
            
            return testItem;
        }

        /// <summary>
        /// 查询所有测试项目
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<IEnumerable<TestItem>> GetAllTestItemsAsync(CancellationToken ct) 
        {
            var itemPos = await _dbContext.BasicItems
                .AsNoTracking()
                .ToListAsync(ct);

            var items = itemPos.Adapt<List<TestItem>>();

            return items;
        }

    }
}
