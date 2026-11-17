namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext
{
    /// <summary>
    /// 用户角色标识常量。
    /// 取值与数据库 <c>user.role</c> 列存储的字符串保持一致，用于替代散落各处的魔法字符串。
    /// </summary>
    public static class UserRoles
    {
        /// <summary>访客：注册后的默认角色。</summary>
        public const string Visitor = "Visitor";

        /// <summary>
        /// 管理员：可执行用户管理与密码重置等敏感操作。
        /// 该值必须与 JWT 中的角色声明一致，才能被 <c>[Authorize(Roles = "Admin")]</c> 识别。
        /// </summary>
        public const string Admin = "Admin";
    }
}
