using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth;
using NX_lims_Softlines_Command_System.src.Application.Service.AuthContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext;

namespace NX_lims_Softlines_Command_System.src.WebApi.Controllers
{
    /// <summary>
    /// 用户认证接口（新架构）。
    /// 路由独立于历史 <c>api/auth</c>，两套接口共存互不影响，便于前端灰度切换。
    /// 本控制器保持极薄：只做协议转换与异常到 ProblemDetails 的映射，业务全部委托 <see cref="AuthAppService"/>。
    /// </summary>
    [ApiController]
    [Route("api/v1/auth")]
    public sealed class UserAuthController : ControllerBase
    {
        private readonly AuthAppService _authAppService;

        /// <summary>
        /// 初始化认证控制器。
        /// </summary>
        /// <param name="authAppService">认证用例服务。</param>
        public UserAuthController(AuthAppService authAppService)
        {
            _authAppService = authAppService;
        }

        /// <summary>登录。</summary>
        /// <param name="req">登录请求，含账号与明文密码。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>令牌对与用户档案。</returns>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto req, CancellationToken ct)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _authAppService.LoginAsync(req, ip, ct);
                return Ok(result);
            }
            catch (AuthException ex)
            {
                return ToProblem(ex);
            }
        }

        /// <summary>刷新令牌。</summary>
        /// <param name="req">刷新令牌请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>新的令牌对与用户档案。</returns>
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto req, CancellationToken ct)
        {
            try
            {
                var result = await _authAppService.RefreshAsync(req, ct);
                return Ok(result);
            }
            catch (AuthException ex)
            {
                return ToProblem(ex);
            }
        }

        /// <summary>注册新账号（角色固定为访客）。</summary>
        /// <param name="req">注册请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>注册结果。</returns>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto req, CancellationToken ct)
        {
            try
            {
                await _authAppService.RegisterAsync(req, ct);
                return Ok(new { success = true });
            }
            catch (AuthException ex)
            {
                return ToProblem(ex);
            }
        }

        /// <summary>
        /// 管理员重置指定用户的密码。
        /// 临时密码仅在本次响应中返回一次，需由管理员当面或电话转达。
        /// </summary>
        /// <param name="req">重置请求，含目标用户账号或工号。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>一次性临时密码。</returns>
        [HttpPost("admin/reset-password")]
        [Authorize(Roles = UserRoles.Admin)]
        public async Task<IActionResult> AdminResetPassword([FromBody] AdminResetPasswordRequestDto req, CancellationToken ct)
        {
            try
            {
                var result = await _authAppService.AdminResetAsync(req, ct);
                return Ok(result);
            }
            catch (AuthException ex)
            {
                return ToProblem(ex);
            }
        }

        /// <summary>用户自助修改密码。</summary>
        /// <param name="req">改密请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>修改结果。</returns>
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto req, CancellationToken ct)
        {
            try
            {
                await _authAppService.ChangePasswordAsync(req, ct);
                return Ok(new { success = true });
            }
            catch (AuthException ex)
            {
                return ToProblem(ex);
            }
        }

        /// <summary>
        /// 把认证业务异常映射为 ProblemDetails 响应，保留错误码与业务标识，
        /// 便于前端按码提示、运维按码聚合；响应体不含任何密码明文。
        /// </summary>
        /// <param name="ex">认证业务异常。</param>
        /// <returns>带 code 与 subject 扩展字段的 ProblemDetails 响应。</returns>
        private ObjectResult ToProblem(AuthException ex)
        {
            var statusCode = ex.Code switch
            {
                AuthErrorCode.InvalidCredential => StatusCodes.Status401Unauthorized,
                AuthErrorCode.InvalidRefreshToken => StatusCodes.Status401Unauthorized,
                AuthErrorCode.AccountLocked => StatusCodes.Status403Forbidden,
                AuthErrorCode.UserNotFound => StatusCodes.Status404NotFound,
                AuthErrorCode.UserAlreadyExists => StatusCodes.Status409Conflict,
                AuthErrorCode.WeakPassword => StatusCodes.Status400BadRequest,
                AuthErrorCode.CurrentPasswordMismatch => StatusCodes.Status400BadRequest,
                AuthErrorCode.PasswordNotChanged => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            };

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = "认证失败",
                Detail = ex.Message,
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = ex.Code;
            if (!string.IsNullOrWhiteSpace(ex.Subject))
            {
                problem.Extensions["subject"] = ex.Subject;
            }

            return new ObjectResult(problem) { StatusCode = statusCode };
        }
    }
}
