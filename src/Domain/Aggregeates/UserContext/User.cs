using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Share;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext
{
    /// <summary>
    /// 用户聚合根。
    /// 维护账号标识、密码哈希与登录状态；密码在聚合内只以哈希形式存在，明文不进入聚合。
    /// 聚合主键 <see cref="UserId"/> 即数据库主键，持久化时序列化为 32 位十六进制字符串写入 user_id 列，
    /// 因此聚合内不再维护第二套持久化标识。
    /// </summary>
    public sealed class User : AggregateRoot<UserId, Guid>
    {
        /// <summary>登录账号（对应 user_name 列），全局唯一。</summary>
        public string UserName { get; private set; } = string.Empty;

        /// <summary>
        /// 密码哈希值（对应 pass_word 列），禁止存放明文。
        /// 算法强度升级依靠 <see cref="IPasswordHasher.Verify"/> 返回的
        /// <see cref="PasswordVerifyResult.SuccessRehashNeeded"/> 驱动。
        /// </summary>
        public string PassWord { get; private set; } = string.Empty;

        /// <summary>角色标识（对应 role 列），取值见 <see cref="UserRoles"/>。</summary>
        public string Role { get; private set; } = string.Empty;

        /// <summary>真实姓名。</summary>
        public string RealName { get; private set; } = string.Empty;

        /// <summary>工号（对应 employee_id 列），可作为登录标识匹配。</summary>
        public string EmployeeId { get; private set; } = string.Empty;

        /// <summary>昵称（对应 nick_name 列），可作为登录标识匹配。</summary>
        public string NickName { get; private set; } = string.Empty;

        /// <summary>账号状态（对应 status 列）：1 表示启用，0 表示锁定或停用。</summary>
        public int Status { get; private set; } = 1;

        /// <summary>连续登录失败次数（对应 login_fail_count 列）。</summary>
        public int LoginFailCount { get; private set; }

        /// <summary>创建时间（对应 create_time 列）。</summary>
        public DateTime? CreateTime { get; private set; }

        /// <summary>最近更新时间（对应 updated_time 列）。</summary>
        public DateTime? UpdatedTime { get; private set; }

        /// <summary>最近登录 IP（对应 last_login_ip 列）。</summary>
        public string? LastLoginIp { get; private set; }

        /// <summary>账号是否处于启用状态。</summary>
        public bool IsEnabled => Status == 1;

        /// <summary>
        /// 构造聚合根实例，供仓储恢复或 <see cref="Create"/> 内部调用。
        /// </summary>
        /// <param name="id">聚合主键，同时是数据库主键。</param>
        /// <param name="userName">登录账号。</param>
        /// <param name="hashedPassword">已哈希的密码值，<b>禁止</b>传入明文。</param>
        /// <param name="role">角色标识。</param>
        /// <param name="realName">真实姓名。</param>
        /// <param name="employeeId">工号。</param>
        /// <param name="nickName">昵称，为空时以账号代替。</param>
        /// <param name="status">账号状态，1 启用、0 锁定。</param>
        /// <param name="loginFailCount">连续登录失败次数。</param>
        /// <param name="createTime">创建时间。</param>
        /// <param name="updatedTime">更新时间。</param>
        /// <param name="lastLoginIp">最近登录 IP。</param>
        public User(
            UserId id,
            string userName,
            string hashedPassword,
            string role,
            string realName,
            string employeeId,
            string? nickName = null,
            int status = 1,
            int loginFailCount = 0,
            DateTime? createTime = null,
            DateTime? updatedTime = null,
            string? lastLoginIp = null)
            : base(id)
        {
            UserName = userName;
            PassWord = hashedPassword;
            Role = role;
            RealName = realName;
            EmployeeId = employeeId;
            NickName = nickName ?? userName;
            Status = status;
            LoginFailCount = loginFailCount;
            CreateTime = createTime;
            UpdatedTime = updatedTime;
            LastLoginIp = lastLoginIp;
        }

        /// <summary>
        /// 创建新用户。明文密码经强度校验并哈希后才进入聚合，聚合内不保留明文。
        /// </summary>
        /// <param name="userName">登录账号。</param>
        /// <param name="plainPassword">明文密码，仅在本方法内使用。</param>
        /// <param name="role">角色标识。</param>
        /// <param name="realName">真实姓名。</param>
        /// <param name="employeeId">工号。</param>
        /// <param name="passwordHasher">密码哈希端口实现，由调用方注入。</param>
        /// <param name="nickName">昵称，为空时以账号代替。</param>
        /// <returns>已持有密码哈希的用户聚合根。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="passwordHasher"/> 为 null。</exception>
        /// <exception cref="ArgumentException">账号、角色为空，或密码不满足 <see cref="PasswordPolicy"/> 的强度要求。</exception>
        public static User Create(
            string userName,
            string plainPassword,
            string role,
            string realName,
            string employeeId,
            IPasswordHasher passwordHasher,
            string? nickName = null)
        {
            if (passwordHasher is null)
            {
                throw new ArgumentNullException(nameof(passwordHasher));
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentException("用户名不能为空", nameof(userName));
            }

            // 强度规则属于领域约束，必须在哈希前拦截，否则弱密码会直接落库
            PasswordPolicy.EnsureValid(plainPassword);

            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("角色不能为空", nameof(role));
            }

            var id = new UserId(Guid.NewGuid());

            return new User(
                id,
                userName,
                passwordHasher.Hash(plainPassword),
                role,
                realName,
                employeeId,
                nickName,
                status: 1,
                loginFailCount: 0,
                createTime: DateTime.Now);
        }

        /// <summary>
        /// 校验明文密码是否与当前密码哈希匹配。
        /// </summary>
        /// <param name="plainPassword">待校验的明文密码。</param>
        /// <param name="passwordHasher">密码哈希端口实现，由调用方注入。</param>
        /// <returns>匹配返回 true；不匹配返回 false。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="passwordHasher"/> 为 null。</exception>
        /// <exception cref="ArgumentException">明文密码为空。</exception>
        public bool VerifyPassword(string plainPassword, IPasswordHasher passwordHasher)
        {
            if (passwordHasher is null)
            {
                throw new ArgumentNullException(nameof(passwordHasher));
            }

            // SuccessRehashNeeded 同样代表密码正确，只是哈希强度需要升级，因此只把 Failed 判为不通过
            return passwordHasher.Verify(PassWord, plainPassword) != PasswordVerifyResult.Failed;
        }

        /// <summary>
        /// 用新的密码哈希替换当前哈希（调用方须已完成原密码校验与强度校验）。
        /// </summary>
        /// <param name="passwordHash">新的密码哈希值。</param>
        /// <exception cref="ArgumentException">哈希为空时抛出。</exception>
        public void ChangePasswordHash(string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new ArgumentException("密码哈希不能为空", nameof(passwordHash));
            }

            PassWord = passwordHash;
            UpdatedTime = DateTime.Now;
        }

        /// <summary>
        /// 修改密码。新密码经强度校验并哈希后替换旧哈希值。
        /// </summary>
        /// <param name="newPlainPassword">新明文密码，仅在本方法内使用。</param>
        /// <param name="passwordHasher">密码哈希端口实现，由调用方注入。</param>
        /// <exception cref="ArgumentNullException"><paramref name="passwordHasher"/> 为 null。</exception>
        /// <exception cref="ArgumentException">新密码不满足 <see cref="PasswordPolicy"/> 的强度要求。</exception>
        public void ChangePassword(string newPlainPassword, IPasswordHasher passwordHasher)
        {
            if (passwordHasher is null)
            {
                throw new ArgumentNullException(nameof(passwordHasher));
            }

            PasswordPolicy.EnsureValid(newPlainPassword);
            ChangePasswordHash(passwordHasher.Hash(newPlainPassword));
        }

        /// <summary>
        /// 记录一次登录成功带来的状态变化：失败计数归零、更新登录时间与来源 IP。
        /// 数据库侧的同等更新由仓储原子 SQL 完成，此处仅保持内存状态一致。
        /// </summary>
        /// <param name="lastLoginIp">登录来源 IP，可为空。</param>
        public void MarkLoginSucceeded(string? lastLoginIp)
        {
            LoginFailCount = 0;
            UpdatedTime = DateTime.Now;
            LastLoginIp = lastLoginIp;
        }
    }
}
