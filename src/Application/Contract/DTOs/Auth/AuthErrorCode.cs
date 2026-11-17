namespace NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth
{
    /// <summary>
    /// 认证用例的稳定错误码。
    /// 前端与运维按码判断与聚合，不依赖易变的异常消息文本。
    /// </summary>
    public static class AuthErrorCode
    {
        /// <summary>账号或密码错误。刻意不区分账号不存在与密码错误，防止账号枚举。</summary>
        public const string InvalidCredential = "AUTH_001";

        /// <summary>账号已锁定或已停用。</summary>
        public const string AccountLocked = "AUTH_002";

        /// <summary>目标用户不存在。</summary>
        public const string UserNotFound = "AUTH_003";

        /// <summary>账号已存在。</summary>
        public const string UserAlreadyExists = "AUTH_004";

        /// <summary>密码强度不满足要求。</summary>
        public const string WeakPassword = "AUTH_005";

        /// <summary>刷新令牌无效或已过期。</summary>
        public const string InvalidRefreshToken = "AUTH_006";

        /// <summary>当前密码不正确。</summary>
        public const string CurrentPasswordMismatch = "AUTH_007";

        /// <summary>新密码与当前密码相同。</summary>
        public const string PasswordNotChanged = "AUTH_008";

        /// <summary>认证基础设施未正确配置（连接串或 JWT 配置缺失）。</summary>
        public const string InfrastructureNotConfigured = "AUTH_009";
    }
}
