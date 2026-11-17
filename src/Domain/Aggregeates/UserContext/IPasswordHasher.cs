using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext
{
    /// <summary>
    /// 密码校验结果。
    /// </summary>
    public enum PasswordVerifyResult
    {
        /// <summary>明文与已存哈希不匹配。</summary>
        Failed = 0,

        /// <summary>校验通过，且哈希强度满足当前算法要求。</summary>
        Success = 1,

        /// <summary>校验通过，但已存哈希强度偏低（旧算法或旧迭代次数），建议按当前算法重新哈希后落库。</summary>
        SuccessRehashNeeded = 2
    }

    /// <summary>
    /// 密码哈希端口。
    /// 接口定义在 Domain 层、实现放在 Infrastructure，目的是让聚合根可以使用哈希能力而不依赖具体算法。
    /// </summary>
    public interface IPasswordHasher:ISingletonDependency
    {
        /// <summary>
        /// 对明文密码生成哈希值。
        /// </summary>
        /// <param name="plainPassword">明文密码；调用方应先用 <see cref="ValueObj.PasswordPolicy.EnsureValid"/> 校验强度。</param>
        /// <returns>可直接落库的哈希字符串，内部包含随机盐，同一明文多次调用结果不同。</returns>
        /// <exception cref="ArgumentException">明文密码为空或仅含空白字符。</exception>
        string Hash(string plainPassword);

        /// <summary>
        /// 校验明文密码与已存哈希是否匹配。
        /// </summary>
        /// <param name="hashedPassword">数据库中已存的哈希值，<b>禁止</b>传入明文。</param>
        /// <param name="plainPassword">待校验的明文密码。</param>
        /// <returns>
        /// 不匹配返回 <see cref="PasswordVerifyResult.Failed"/>；
        /// 匹配且强度足够返回 <see cref="PasswordVerifyResult.Success"/>；
        /// 匹配但需升级算法时返回 <see cref="PasswordVerifyResult.SuccessRehashNeeded"/>。
        /// </returns>
        /// <exception cref="ArgumentException">任一参数为空或仅含空白字符。</exception>
        PasswordVerifyResult Verify(string hashedPassword, string plainPassword);
    }
}
