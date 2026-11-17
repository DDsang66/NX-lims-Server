using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth;
using NX_lims_Softlines_Command_System.src.Application.Interface.Auth;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Application.Service.AuthContext
{
    /// <summary>
    /// 认证用例编排服务。
    /// 只做流程编排与错误码翻译，业务规则（密码强度、哈希、角色）全部委托给领域与端口实现；
    /// 密码明文仅在方法栈内短暂存在，禁止写入日志、异常消息或返回体（管理员临时密码除外，仅一次）。
    /// </summary>
    public sealed class AuthAppService : IScopedDependency
    {
        /// <summary>临时密码长度。低熵临时密码容易被暴力枚举，故取 16 位。</summary>
        private const int TemporaryPasswordLength = 16;

        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthAppService> _logger;

        /// <summary>
        /// 初始化认证用例服务。
        /// </summary>
        /// <param name="userRepository">用户持久化端口。</param>
        /// <param name="passwordHasher">密码哈希端口。</param>
        /// <param name="tokenService">令牌签发端口。</param>
        /// <param name="logger">日志器，禁止记录任何密码明文。</param>
        public AuthAppService(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            ILogger<AuthAppService> logger)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _logger = logger;
        }

        /// <summary>
        /// 登录：校验凭证、维护失败计数与锁定状态、签发令牌。
        /// </summary>
        /// <param name="req">登录请求，含账号与明文密码。</param>
        /// <param name="lastLoginIp">登录来源 IP，可为空。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>令牌对与用户档案。</returns>
        /// <exception cref="AuthException">
        /// <see cref="AuthErrorCode.InvalidCredential"/>：账号或密码错误；
        /// <see cref="AuthErrorCode.AccountLocked"/>：账号已锁定或停用。
        /// </exception>
        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto req, string? lastLoginIp, CancellationToken ct)
        {
            var userName = req.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(req.Password))
            {
                // 不区分“账号为空”与“密码错误”，避免通过错误消息枚举有效账号
                throw new AuthException(AuthErrorCode.InvalidCredential, "账号或密码错误", userName);
            }

            var user = await _userRepository.FindByUserNameAsync(userName, ct);

            // 用户不存在同样返回凭证错误，避免账号枚举
            if (user is null)
            {
                throw new AuthException(AuthErrorCode.InvalidCredential, "账号或密码错误", userName);
            }

            if (!user.IsEnabled)
            {
                throw new AuthException(
                    AuthErrorCode.AccountLocked,
                    "账号已锁定或已停用，请联系管理员重置密码后解锁",
                    userName);
            }

            if (!user.VerifyPassword(req.Password, _passwordHasher))
            {
                // 失败计数由数据库原子自增，避免并发下的读改写丢失
                var failCount = await _userRepository.RegisterLoginFailureAsync(
                    user.Id,
                    UserLoginPolicy.LockThreshold,
                    ct);

                if (failCount >= UserLoginPolicy.LockThreshold)
                {
                    _logger.LogWarning("账号 {UserName} 连续登录失败 {FailCount} 次，已锁定", user.UserName, failCount);
                }

                // 返回值不暴露“这是第几次失败”，防止攻击者据此判断账号是否存在
                throw new AuthException(AuthErrorCode.InvalidCredential, "账号或密码错误", userName);
            }

            await _userRepository.ResetLoginFailureAsync(user.Id, lastLoginIp, ct);
            user.MarkLoginSucceeded(lastLoginIp);

            return BuildLoginResponse(user);
        }

        /// <summary>
        /// 刷新令牌：验签、校验有效期与令牌类型，并确认账号仍启用后重新签发。
        /// </summary>
        /// <param name="req">刷新令牌请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>新的令牌对与用户档案。</returns>
        /// <exception cref="AuthException">
        /// <see cref="AuthErrorCode.InvalidRefreshToken"/>：令牌无效、过期、类型不符或用户标识不可解析；
        /// <see cref="AuthErrorCode.AccountLocked"/>：账号已锁定或停用。
        /// </exception>
        public async Task<LoginResponseDto> RefreshAsync(RefreshTokenRequestDto req, CancellationToken ct)
        {
            if (!_tokenService.TryValidateRefreshToken(req.RefreshToken, out var userIdText))
            {
                throw new AuthException(AuthErrorCode.InvalidRefreshToken, "刷新令牌无效或已过期，请重新登录");
            }

            // 聚合主键为 Guid，令牌内的 uid 是其字符串形式；解析失败说明令牌被篡改或格式不兼容
            if (!Guid.TryParse(userIdText, out var userIdValue))
            {
                throw new AuthException(AuthErrorCode.InvalidRefreshToken, "刷新令牌中的用户标识无效，请重新登录");
            }

            // 角色与启用状态一律回库读取，不采信令牌内的旧值
            var user = await _userRepository.GetByIdAsync(new UserId(userIdValue), ct);
            if (user is null)
            {
                throw new AuthException(AuthErrorCode.InvalidRefreshToken, "账号不存在，请重新登录", userIdText);
            }

            if (!user.IsEnabled)
            {
                throw new AuthException(
                    AuthErrorCode.AccountLocked,
                    "账号已锁定或已停用，请联系管理员重置密码后解锁",
                    user.UserName);
            }

            return BuildLoginResponse(user);
        }

        /// <summary>
        /// 注册：账号唯一性校验、密码强度校验与哈希落库，角色固定为访客。
        /// </summary>
        /// <param name="req">注册请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <exception cref="AuthException">
        /// <see cref="AuthErrorCode.UserAlreadyExists"/>：账号已存在；
        /// <see cref="AuthErrorCode.WeakPassword"/>：密码强度不足。
        /// </exception>
        public async Task RegisterAsync(RegisterRequestDto req, CancellationToken ct)
        {
            var userName = req.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new AuthException(AuthErrorCode.WeakPassword, "账号不能为空");
            }

            if (await _userRepository.ExistsByUserNameAsync(userName, ct))
            {
                throw new AuthException(AuthErrorCode.UserAlreadyExists, "账号已存在", userName);
            }

            // 先做强度校验再哈希：把领域规则失败翻译成稳定错误码，便于前端统一提示
            try
            {
                PasswordPolicy.EnsureValid(req.Password);
            }
            catch (ArgumentException ex)
            {
                throw new AuthException(AuthErrorCode.WeakPassword, ex.Message, userName, ex);
            }

            var user = User.Create(
                userName,
                req.Password,
                UserRoles.Visitor,
                req.NickName ?? string.Empty,
                req.EmployeeId ?? string.Empty,
                _passwordHasher,
                req.NickName);

            await _userRepository.AddAsync(user, ct);

            // 只记录账号标识，绝不记录密码与哈希
            _logger.LogInformation("新账号注册成功：{UserName}（userId={UserId}）", user.UserName, ToPublicId(user));
        }

        /// <summary>
        /// 管理员重置密码：系统生成一次性临时密码并哈希落库，明文仅在本次返回值中出现。
        /// </summary>
        /// <param name="req">重置请求，含目标用户标识。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>临时密码响应，调用方需一次性转达给用户。</returns>
        /// <exception cref="AuthException"><see cref="AuthErrorCode.UserNotFound"/>：目标用户不存在。</exception>
        public async Task<AdminResetPasswordResponseDto> AdminResetAsync(AdminResetPasswordRequestDto req, CancellationToken ct)
        {
            var identifier = req.UserIdOrName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new AuthException(AuthErrorCode.UserNotFound, "必须指定要重置密码的账号或工号");
            }

            var user = await _userRepository.FindByIdentifierAsync(identifier, ct);
            if (user is null)
            {
                throw new AuthException(AuthErrorCode.UserNotFound, $"用户 {identifier} 不存在", identifier);
            }

            var tempPassword = GenerateTemporaryPassword();
            await _userRepository.UpdatePasswordAsync(user.Id, _passwordHasher.Hash(tempPassword), ct);

            // 审计日志只记录操作对象，临时密码明文绝不入库、绝不写日志
            _logger.LogInformation("管理员重置了用户 {UserName}（userId={UserId}）的密码", user.UserName, ToPublicId(user));

            return new AdminResetPasswordResponseDto(ToPublicId(user), tempPassword, null);
        }

        /// <summary>
        /// 自助修改密码：校验当前密码、新密码强度与新旧差异后更新哈希。
        /// </summary>
        /// <param name="req">改密请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <exception cref="AuthException">
        /// <see cref="AuthErrorCode.UserNotFound"/>：用户不存在；
        /// <see cref="AuthErrorCode.CurrentPasswordMismatch"/>：当前密码不正确；
        /// <see cref="AuthErrorCode.PasswordNotChanged"/>：新密码与当前密码相同；
        /// <see cref="AuthErrorCode.WeakPassword"/>：新密码强度不足。
        /// </exception>
        public async Task ChangePasswordAsync(ChangePasswordRequestDto req, CancellationToken ct)
        {
            var identifier = req.UserNameOrEmployeeId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new AuthException(AuthErrorCode.UserNotFound, "必须指定当前用户账号或工号");
            }

            var user = await _userRepository.FindByIdentifierAsync(identifier, ct);
            if (user is null)
            {
                throw new AuthException(AuthErrorCode.UserNotFound, $"用户 {identifier} 不存在", identifier);
            }

            if (!user.VerifyPassword(req.CurrentPassword, _passwordHasher))
            {
                throw new AuthException(AuthErrorCode.CurrentPasswordMismatch, "当前密码不正确", identifier);
            }

            if (string.Equals(req.CurrentPassword, req.NewPassword, StringComparison.Ordinal))
            {
                throw new AuthException(AuthErrorCode.PasswordNotChanged, "新密码不能与当前密码相同", identifier);
            }

            try
            {
                PasswordPolicy.EnsureValid(req.NewPassword);
            }
            catch (ArgumentException ex)
            {
                throw new AuthException(AuthErrorCode.WeakPassword, ex.Message, identifier, ex);
            }

            user.ChangePasswordHash(_passwordHasher.Hash(req.NewPassword));
            await _userRepository.UpdatePasswordAsync(user.Id, user.PassWord, ct);

            _logger.LogInformation("用户 {UserName} 修改了自己的密码", user.UserName);
        }

        /// <summary>
        /// 组装登录响应：签发令牌并投影用户档案，投影中不带任何密码字段。
        /// </summary>
        /// <param name="user">已通过校验的用户聚合。</param>
        /// <returns>登录响应。</returns>
        private LoginResponseDto BuildLoginResponse(User user)
        {
            var publicUserId = ToPublicId(user);
            var tokens = _tokenService.IssueTokens(publicUserId, user.UserName, user.Role);
            var profile = new UserProfileDto(publicUserId, user.UserName, user.NickName, user.EmployeeId, user.Role);

            return new LoginResponseDto(
                tokens.AccessToken,
                tokens.RefreshToken,
                tokens.AccessTokenExpiresAt,
                profile);
        }

        /// <summary>
        /// 把聚合主键转为对外暴露的字符串形式，与数据库 user_id 列的存储形式保持一致。
        /// </summary>
        /// <param name="user">用户聚合。</param>
        /// <returns>32 位十六进制字符串。</returns>
        private static string ToPublicId(User user) => user.Id.Value.ToString("N");

        /// <summary>
        /// 生成一次性临时密码。
        /// 先用密码学安全随机源保证大小写字母、数字、符号四类各出现一次以满足强度策略，
        /// 再整体打乱顺序避免前几位出现固定规律；同时剔除易混淆字符，降低人工转述出错概率。
        /// </summary>
        /// <returns>可直接用于一次性登录的明文临时密码。</returns>
        private static string GenerateTemporaryPassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%^&*-_=+";
            const string all = upper + lower + digits + symbols;

            var chars = new char[TemporaryPasswordLength];
            chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
            chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
            chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

            for (var i = 4; i < TemporaryPasswordLength; i++)
            {
                chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
            }

            for (var i = TemporaryPasswordLength - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars);
        }
    }
}
