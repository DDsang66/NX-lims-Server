using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth;

namespace NX_lims_Softlines_Command_System.src.Application.Interface.Auth
{
    /// <summary>
    /// 令牌签发与校验端口。
    /// 实现方负责具体的 JWT 库、密钥读取与 claim 结构；Application 只依赖本抽象，便于单测替换。
    /// </summary>
    public interface ITokenService
    {
        /// <summary>为指定用户签发访问令牌与刷新令牌。</summary>
        /// <param name="userId">用户唯一标识，写入 uid 声明。</param>
        /// <param name="userName">登录账号，写入 name 声明。</param>
        /// <param name="role">角色标识，写入角色声明，供 <c>[Authorize(Roles = ...)]</c> 判定。</param>
        /// <returns>令牌对，含各自过期时间。</returns>
        /// <exception cref="InvalidOperationException">JWT 相关配置缺失时抛出。</exception>
        TokenPair IssueTokens(string userId, string userName, string role);

        /// <summary>
        /// 校验刷新令牌的签名、有效期与令牌类型。
        /// 仅接受 token_type 为 refresh 的令牌，拒绝用访问令牌换取新令牌。
        /// </summary>
        /// <param name="refreshToken">待校验的刷新令牌串。</param>
        /// <param name="userId">校验通过时输出 uid 声明值；校验失败输出空字符串。</param>
        /// <returns>校验通过返回 true。</returns>
        bool TryValidateRefreshToken(string refreshToken, out string userId);
    }
}
