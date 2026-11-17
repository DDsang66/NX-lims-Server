namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext.ValueObj
{
    /// <summary>
    /// 密码强度策略。
    /// 只承载与存储方式无关的领域规则，不涉及哈希算法，因此可被聚合根直接调用。
    /// </summary>
    public static class PasswordPolicy
    {
        /// <summary>密码最小长度。</summary>
        public const int MinLength = 8;

        /// <summary>密码最大长度，用于限制超长输入带来的哈希计算开销。</summary>
        public const int MaxLength = 128;

        /// <summary>
        /// 校验明文密码是否满足强度要求，不满足时抛出带具体原因的异常。
        /// </summary>
        /// <param name="plainPassword">待校验的明文密码。</param>
        /// <exception cref="ArgumentException">
        /// 密码为空、长度超出 [<see cref="MinLength"/>, <see cref="MaxLength"/>] 范围、
        /// 或不同时包含字母与数字时抛出；消息中携带可直接展示给操作人员的原因。
        /// </exception>
        public static void EnsureValid(string plainPassword)
        {
            if (string.IsNullOrWhiteSpace(plainPassword))
            {
                throw new ArgumentException("密码不能为空", nameof(plainPassword));
            }

            if (plainPassword.Length < MinLength)
            {
                throw new ArgumentException($"密码长度不能少于 {MinLength} 位", nameof(plainPassword));
            }

            if (plainPassword.Length > MaxLength)
            {
                throw new ArgumentException($"密码长度不能超过 {MaxLength} 位", nameof(plainPassword));
            }

            // 要求字母与数字并存：单一字符类别的密码空间太小，容易被字典穷举
            bool hasLetter = false;
            bool hasDigit = false;
            foreach (char c in plainPassword)
            {
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                }
                else if (char.IsDigit(c))
                {
                    hasDigit = true;
                }

                if (hasLetter && hasDigit)
                {
                    break;
                }
            }

            if (!hasLetter)
            {
                throw new ArgumentException("密码必须包含字母", nameof(plainPassword));
            }

            if (!hasDigit)
            {
                throw new ArgumentException("密码必须包含数字", nameof(plainPassword));
            }
        }
    }
}
