using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TestItemContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Domain.Share.Interface;

namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Repository
{
    /// <summary>
    /// 用户聚合的持久化端口。
    /// 实现方负责与具体数据库交互（本项目由基础设施层用参数化 SQL 实现）；
    /// Domain 与 Application 只依赖本抽象，不得感知 SQL 方言与连接方式。
    /// </summary>
    /// <remarks>
    /// 所有方法均以聚合主键 <see cref="UserId"/> 作为持久化标识，
    /// 实现方负责把它序列化为数据库 user_id 列的存储形式（32 位十六进制字符串）。
    /// </remarks>
    public interface IUserRepository : IScopedDependency, IRepository<User, UserId, Guid>
    {
        /// <summary>按登录账号精确查询用户。</summary>
        /// <param name="userName">登录账号（对应 user_name 列）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>找到返回用户聚合；未找到返回 null。</returns>
        Task<User?> FindByUserNameAsync(string userName, CancellationToken ct);

        /// <summary>按用户聚合主键查询用户。</summary>
        /// <param name="id">用户聚合主键。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>找到返回用户聚合；未找到返回 null。</returns>
        Task<User?> GetByIdAsync(UserId id, CancellationToken ct);

        /// <summary>
        /// 按「账号 / 工号 / 昵称」任一维度匹配用户，保留历史密码重置接口的多字段定位口径。
        /// </summary>
        /// <param name="identifier">账号、工号或昵称之一。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>找到返回用户聚合；未找到返回 null。</returns>
        Task<User?> FindByIdentifierAsync(string identifier, CancellationToken ct);

        /// <summary>判断登录账号是否已存在。</summary>
        /// <param name="userName">登录账号。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>存在返回 true。</returns>
        Task<bool> ExistsByUserNameAsync(string userName, CancellationToken ct);

        /// <summary>新增用户记录。</summary>
        /// <param name="user">待持久化的用户聚合，其 <see cref="User.PassWord"/> 必须是哈希值。</param>
        /// <param name="ct">取消令牌。</param>
        Task AddAsync(User user, CancellationToken ct);

        /// <summary>更新指定用户的密码哈希。</summary>
        /// <param name="id">用户聚合主键。</param>
        /// <param name="passwordHash">新的密码哈希值。</param>
        /// <param name="ct">取消令牌。</param>
        Task UpdatePasswordAsync(UserId id, string passwordHash, CancellationToken ct);

        /// <summary>
        /// 总体更新
        /// </summary>
        /// <param name="user"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(User user, CancellationToken ct);

        /// <summary>
        /// 原子登记一次登录失败：失败计数加一，累计达到阈值时同时把账号状态置为锁定。
        /// </summary>
        /// <param name="id">用户聚合主键。</param>
        /// <param name="lockThreshold">锁定阈值，调用方应传 <see cref="UserLoginPolicy.LockThreshold"/>。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>本次更新后的累计失败次数；用户不存在返回 0。</returns>
        Task<int> RegisterLoginFailureAsync(UserId id, int lockThreshold, CancellationToken ct);

        /// <summary>原子登记一次登录成功：失败计数归零，并更新登录时间与来源 IP。</summary>
        /// <param name="id">用户聚合主键。</param>
        /// <param name="lastLoginIp">登录来源 IP，可为空。</param>
        /// <param name="ct">取消令牌。</param>
        Task ResetLoginFailureAsync(UserId id, string? lastLoginIp, CancellationToken ct);
    }
}
