namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext
{
    /// <summary>
    /// 用户登录策略常量。
    /// 锁定阈值在此单点定义，应用层编排与基础设施 SQL 都从这里取值，避免阈值散落各处导致行为漂移。
    /// </summary>
    public static class UserLoginPolicy
    {
        /// <summary>连续登录失败达到该次数后，账号状态会被置为锁定（status = 0）。</summary>
        public const int LockThreshold = 5;
    }
}
