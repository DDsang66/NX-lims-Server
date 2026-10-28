using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Domain.Share.Interface;

namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Repository
{
    public interface ITestItemRepository:IScopedDependency,IRepository<TestItem,TestItemId,string>
    {
        /// <summary>
        /// 添加聚合根
        /// </summary>
        /// <param name="aggregateRoot"></param>
        /// <returns></returns>
        Task AddAsync(TestItem aggregateRoot, CancellationToken ct);

        /// <summary>
        /// 修改聚合根
        /// </summary>
        /// <param name="aggregateRoot"></param>
        /// <returns></returns>
        Task UpdateAsync(TestItem aggregateRoot, CancellationToken ct);

        /// <summary>
        /// 查询聚合根
        /// </summary>
        /// <param name="aggregateRootId"></param>
        /// <param name="ct"></param>
        /// <returns>聚合根</returns>
        Task<TestItem> GetByIdAsync(TestItemId aggregateRootId, CancellationToken ct);

        /// <summary>
        /// 查询聚合根
        /// </summary>
        /// <param name="aggregateRootId"></param>
        /// <param name="ct"></param>
        /// <returns>聚合根</returns>
        Task<IEnumerable<TestItem>> GetAllTestItemsAsync(CancellationToken ct);
    }
}
