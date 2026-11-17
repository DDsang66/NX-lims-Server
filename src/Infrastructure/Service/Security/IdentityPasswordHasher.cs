using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.Service.Security
{
    /// <summary>
    /// 基于 ASP.NET Core Identity (PBKDF2) 的密码哈希实现。
    /// 通过 <see cref="IPasswordHasher"/> 端口对外提供能力，Domain 层因此不依赖具体算法。
    /// </summary>
    public sealed class IdentityPasswordHasher : IPasswordHasher, ISingletonDependency
    {
        /// <summary>
        /// PBKDF2 迭代次数，与 ASP.NET Core Identity V3 默认值保持一致。
        /// 该值参与哈希头部写入，升级后旧哈希会由框架自动判定为需要重新哈希。
        /// </summary>
        private const int IterationCount = 100_000;

        /// <summary>
        /// ASP.NET Core 的密码哈希器。无状态，可安全复用。
        /// <see cref="PasswordHasher{TUser}.HashPassword"/> 只是需要一个用户对象占位，不读取其内容，
        /// 因此这里统一传 null。
        /// </summary>
        private readonly PasswordHasher<object?> _hasher;

        /// <summary>
        /// 初始化哈希器，并固定使用 IdentityV3 兼容模式（PBKDF2-HMAC-SHA256）。
        /// </summary>
        public IdentityPasswordHasher()
        {
            _hasher = new PasswordHasher<object?>(Options.Create(new PasswordHasherOptions
            {
                // V3 模式在遇到数据库中 0x00 开头的旧版哈希时，
                // 框架会先按 V2 算法校验，通过后返回 SuccessRehashNeeded，从而实现无感升级
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = IterationCount
            }));
        }

        /// <summary>
        /// 对明文密码生成哈希值。
        /// </summary>
        /// <param name="plainPassword">明文密码，调用方应先用 <c>PasswordPolicy.EnsureValid</c> 校验强度。</param>
        /// <returns>可直接落库的哈希字符串，内含随机盐。</returns>
        /// <exception cref="ArgumentException">明文密码为空或仅含空白字符。</exception>
        public string Hash(string plainPassword)
        {
            if (string.IsNullOrWhiteSpace(plainPassword))
            {
                throw new ArgumentException("密码不能为空", nameof(plainPassword));
            }

            return _hasher.HashPassword(null, plainPassword);
        }

        /// <summary>
        /// 校验明文密码与已存哈希是否匹配。
        /// </summary>
        /// <param name="hashedPassword">数据库中已存的哈希值，禁止传入明文。</param>
        /// <param name="plainPassword">待校验的明文密码。</param>
        /// <returns>
        /// 匹配且强度足够返回 <see cref="PasswordVerifyResult.Success"/>；
        /// 匹配但为旧算法/旧迭代次数生成的哈希时返回 <see cref="PasswordVerifyResult.SuccessRehashNeeded"/>；
        /// 其余情况返回 <see cref="PasswordVerifyResult.Failed"/>。
        /// </returns>
        /// <exception cref="ArgumentException">任一参数为空或仅含空白字符。</exception>
        public PasswordVerifyResult Verify(string hashedPassword, string plainPassword)
        {
            if (string.IsNullOrWhiteSpace(hashedPassword))
            {
                throw new ArgumentException("已存哈希不能为空", nameof(hashedPassword));
            }

            if (string.IsNullOrWhiteSpace(plainPassword))
            {
                throw new ArgumentException("密码不能为空", nameof(plainPassword));
            }

            PasswordVerificationResult result;
            try
            {
                result = _hasher.VerifyHashedPassword(null, hashedPassword, plainPassword);
            }
            catch (FormatException)
            {
                // 库中存量数据可能是明文或损坏的 Base64：
                // 这里按“校验不通过”处理，避免脏数据把登录接口打成 500，
                // 同时让调用方的失败计数与账号锁定逻辑继续生效
                return PasswordVerifyResult.Failed;
            }

            return result switch
            {
                PasswordVerificationResult.Success => PasswordVerifyResult.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerifyResult.SuccessRehashNeeded,
                _ => PasswordVerifyResult.Failed
            };
        }
    }
}
