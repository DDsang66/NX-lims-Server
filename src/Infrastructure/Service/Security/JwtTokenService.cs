using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth;
using NX_lims_Softlines_Command_System.src.Application.Interface.Auth;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.Service.Security
{
    /// <summary>
    /// 基于 JWT 的令牌签发与校验实现。
    /// 与老架构的 JwtService 刻意隔离：不共用类型、不共用代码，
    /// 且刷新令牌必须验签并校验令牌类型，修掉老实现只解析不验签的缺陷。
    /// </summary>
    public sealed class JwtTokenService : ITokenService, ISingletonDependency
    {
        /// <summary>uid 声明的键名，与前端既有解析逻辑保持一致。</summary>
        private const string UidClaim = "uid";

        /// <summary>令牌类型声明的键名，用于区分访问令牌与刷新令牌。</summary>
        private const string TokenTypeClaim = "token_type";

        /// <summary>刷新令牌的类型取值。</summary>
        private const string RefreshTokenType = "refresh";

        private readonly string _issuer;
        private readonly string _audience;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly int _expireMinutes;
        private readonly int _refreshExpireHours;
        private readonly ILogger<JwtTokenService> _logger;

        /// <summary>
        /// JWT 处理器。内部无状态调用，可安全复用。
        /// </summary>
        private readonly JwtSecurityTokenHandler _handler = new();

        /// <summary>
        /// 初始化令牌服务并读取 JWT 配置。
        /// </summary>
        /// <param name="configuration">配置源，读取 <c>Jwt</c> 配置节。</param>
        /// <param name="logger">日志器，只记录令牌类型与失败原因分类，不记录令牌内容。</param>
        /// <exception cref="InvalidOperationException">Issuer、Audience、Secret 任一缺失时抛出。</exception>
        public JwtTokenService(IConfiguration configuration, ILogger<JwtTokenService> logger)
        {
            _logger = logger;
            var jwt = configuration.GetSection("Jwt");

            _issuer = jwt["Issuer"] ?? throw new InvalidOperationException("未配置 Jwt:Issuer，无法签发令牌");
            _audience = jwt["Audience"] ?? throw new InvalidOperationException("未配置 Jwt:Audience，无法签发令牌");

            var secret = jwt["Secret"] ?? throw new InvalidOperationException("未配置 Jwt:Secret，无法签发令牌");
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            // 有效期读既有配置键；缺失时取保守默认值并在日志中提示一次，避免线上配置遗漏导致运行期异常
            _expireMinutes = int.TryParse(jwt["ExpireMinutes"], out var minutes) ? minutes : 120;
            _refreshExpireHours = int.TryParse(jwt["RefreshExpireHours"], out var hours) ? hours : 168;
        }

        /// <summary>为指定用户签发访问令牌与刷新令牌。</summary>
        /// <param name="userId">用户唯一标识，写入 uid 声明。</param>
        /// <param name="userName">登录账号，写入 name 声明。</param>
        /// <param name="role">角色标识，写入角色声明。</param>
        /// <returns>令牌对，含各自过期时间。</returns>
        public TokenPair IssueTokens(string userId, string userName, string role)
        {
            var now = DateTime.UtcNow;
            var accessExpiresAt = now.AddMinutes(_expireMinutes);
            var refreshExpiresAt = now.AddHours(_refreshExpireHours);

            var accessClaims = new List<Claim>
            {
                new(UidClaim, userId),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };
            if (!string.IsNullOrWhiteSpace(userName))
            {
                accessClaims.Add(new Claim(JwtRegisteredClaimNames.Name, userName));
            }

            // 角色声明双写：ClaimTypes.Role（长 URI）与短名 role。
            // JwtBearer 的入站映射表只把短名映射为长 URI，两种都写可兼容任意一侧的读取方式，
            // 从而避免为了鉴权再去改动 Program.cs 里的 TokenValidationParameters。
            if (!string.IsNullOrWhiteSpace(role))
            {
                accessClaims.Add(new Claim(ClaimTypes.Role, role));
                accessClaims.Add(new Claim("role", role));
            }

            var refreshClaims = new List<Claim>
            {
                new(UidClaim, userId),
                new(TokenTypeClaim, RefreshTokenType),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            var accessToken = WriteToken(accessClaims, accessExpiresAt);
            var refreshToken = WriteToken(refreshClaims, refreshExpiresAt);

            return new TokenPair(accessToken, refreshToken, accessExpiresAt, refreshExpiresAt);
        }

        /// <summary>
        /// 校验刷新令牌的签名、有效期与令牌类型。
        /// </summary>
        /// <param name="refreshToken">待校验的刷新令牌串。</param>
        /// <param name="userId">校验通过时输出 uid 声明值；失败输出空字符串。</param>
        /// <returns>校验通过返回 true。</returns>
        public bool TryValidateRefreshToken(string refreshToken, out string userId)
        {
            userId = string.Empty;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return false;
            }

            try
            {
                var principal = _handler.ValidateToken(refreshToken, BuildValidationParameters(), out _);

                // 必须校验令牌类型：否则攻击者可拿访问令牌当作刷新令牌无限续期
                var tokenType = principal.FindFirst(TokenTypeClaim)?.Value;
                if (!string.Equals(tokenType, RefreshTokenType, StringComparison.Ordinal))
                {
                    _logger.LogWarning("刷新令牌校验失败：令牌类型不是 refresh");
                    return false;
                }

                var uid = principal.FindFirst(UidClaim)?.Value;
                if (string.IsNullOrWhiteSpace(uid))
                {
                    _logger.LogWarning("刷新令牌校验失败：缺少 uid 声明");
                    return false;
                }

                userId = uid;
                return true;
            }
            catch (SecurityTokenException ex)
            {
                // 只记录异常类型，不记录令牌内容，避免令牌泄漏到日志
                _logger.LogWarning("刷新令牌校验失败：{Reason}", ex.GetType().Name);
                return false;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("刷新令牌格式非法：{Reason}", ex.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// 构造令牌校验参数。时钟偏移收敛到 1 分钟，避免服务器间微小时间差造成刚签发的令牌被判过期。
        /// </summary>
        /// <returns>令牌校验参数。</returns>
        private TokenValidationParameters BuildValidationParameters() => new()
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role"
        };

        /// <summary>
        /// 把声明集合写成签名后的令牌串。
        /// </summary>
        /// <param name="claims">声明集合。</param>
        /// <param name="expiresAt">过期时间（UTC）。</param>
        /// <returns>JWT 字符串。</returns>
        private string WriteToken(IEnumerable<Claim> claims, DateTime expiresAt)
        {
            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAt,
                signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

            return _handler.WriteToken(token);
        }
    }
}
