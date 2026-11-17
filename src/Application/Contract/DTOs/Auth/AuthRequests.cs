namespace NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth
{
    /// <summary>登录请求。</summary>
    /// <param name="UserName">登录账号。</param>
    /// <param name="Password">明文密码，仅在校验期间存在于内存，禁止写入日志或异常消息。</param>
    public sealed record LoginRequestDto(string UserName, string Password);

    /// <summary>刷新令牌请求。</summary>
    /// <param name="RefreshToken">刷新令牌串。</param>
    public sealed record RefreshTokenRequestDto(string RefreshToken);

    /// <summary>注册请求。</summary>
    /// <param name="UserName">登录账号。</param>
    /// <param name="Password">明文密码，需满足 <c>PasswordPolicy</c> 强度要求。</param>
    /// <param name="NickName">昵称。</param>
    /// <param name="EmployeeId">工号。</param>
    public sealed record RegisterRequestDto(string UserName, string Password, string NickName, string EmployeeId);

    /// <summary>管理员重置用户密码请求。请求中不传密码，临时密码由系统生成。</summary>
    /// <param name="UserIdOrName">目标用户的账号或工号。</param>
    public sealed record AdminResetPasswordRequestDto(string UserIdOrName);

    /// <summary>用户自助修改密码请求。</summary>
    /// <param name="UserNameOrEmployeeId">当前用户的账号或工号。</param>
    /// <param name="CurrentPassword">当前密码（管理员重置后即为临时密码）。</param>
    /// <param name="NewPassword">新密码，需满足密码强度要求。</param>
    public sealed record ChangePasswordRequestDto(string UserNameOrEmployeeId, string CurrentPassword, string NewPassword);
}
