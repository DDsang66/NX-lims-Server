namespace NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth
{
    /// <summary>
    /// 用户档案信息。刻意不包含任何密码字段（明文或哈希），可安全返回给前端。
    /// </summary>
    /// <param name="UserId">用户唯一标识。</param>
    /// <param name="UserName">登录账号。</param>
    /// <param name="NickName">昵称。</param>
    /// <param name="EmployeeId">工号。</param>
    /// <param name="Role">角色标识。</param>
    public sealed record UserProfileDto(string UserId, string UserName, string NickName, string EmployeeId, string Role);

    /// <summary>登录或刷新的响应体。</summary>
    /// <param name="AccessToken">访问令牌。</param>
    /// <param name="RefreshToken">刷新令牌。</param>
    /// <param name="AccessTokenExpiresAt">访问令牌过期时间（UTC）。</param>
    /// <param name="User">用户档案。</param>
    public sealed record LoginResponseDto(
        string AccessToken,
        string RefreshToken,
        DateTime AccessTokenExpiresAt,
        UserProfileDto User);

    /// <summary>令牌签发结果。</summary>
    /// <param name="AccessToken">访问令牌。</param>
    /// <param name="RefreshToken">刷新令牌。</param>
    /// <param name="AccessTokenExpiresAt">访问令牌过期时间（UTC）。</param>
    /// <param name="RefreshTokenExpiresAt">刷新令牌过期时间（UTC）。</param>
    public sealed record TokenPair(
        string AccessToken,
        string RefreshToken,
        DateTime AccessTokenExpiresAt,
        DateTime RefreshTokenExpiresAt);

    /// <summary>
    /// 管理员重置密码的响应体。临时密码<b>仅在本响应中出现一次</b>，
    /// 不入库、不写日志，由管理员当面或电话转达给用户。
    /// </summary>
    /// <param name="UserId">被重置用户的唯一标识。</param>
    /// <param name="TempPassword">一次性临时密码明文，仅此一次返回。</param>
    /// <param name="ExpiresAt">临时密码建议失效时间，为空表示由业务侧口头约定时限。</param>
    public sealed record AdminResetPasswordResponseDto(string UserId, string TempPassword, DateTime? ExpiresAt);
}
