using NX_lims_Softlines_Command_System.Domain.Model.Entities;

namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Repository.FiberContext
{
    public interface IFiberDatabaseRepository
    {
        /// <summary>
        /// 获取所有纤维数据
        /// </summary>
        Task<List<CompositionNew>> GetAllAsync();

        /// <summary>
        /// 根据ID获取纤维数据
        /// </summary>
        Task<CompositionNew?> GetByIdAsync(Guid id);

        /// <summary>
        /// 根据英文名称获取纤维数据
        /// </summary>
        Task<CompositionNew?> GetByNameEnAsync(string nameEn);

        /// <summary>
        /// 添加纤维数据
        /// </summary>
        Task<CompositionNew> AddAsync(CompositionNew fiber);

        /// <summary>
        /// 更新纤维数据
        /// </summary>
        Task<CompositionNew> UpdateAsync(CompositionNew fiber);

        /// <summary>
        /// 删除纤维数据
        /// </summary>
        Task<bool> DeleteAsync(Guid id);

        /// <summary>
        /// 获取所有纤维名称列表（用于前端下拉选择）
        /// </summary>
        Task<List<string>> GetAllNamesAsync();

        /// <summary>
        /// 获取回潮率映射（纤维名 → 回潮率%），根据标准选对应列
        /// </summary>
        Task<Dictionary<string, decimal>> GetMoistureRegainMapAsync(string standard);

        /// <summary>
        /// 获取中文名映射（纤维英文名 → 中文名）。国标报告上的纤维名要印成 `English中文`。
        /// 与回潮率读的是同一张 fiber_database，只是换个列。
        /// </summary>
        Task<Dictionary<string, string>> GetChineseNameMapAsync();
    }
}
