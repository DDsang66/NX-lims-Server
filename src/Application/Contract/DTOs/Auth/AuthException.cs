namespace NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.Auth
{
    /// <summary>
    /// 认证用例的可读业务异常。
    /// 携带稳定错误码与业务标识，供接口层映射为 ProblemDetails，便于操作人员据原因协调运维处理；
    /// 异常消息中<b>不得</b>出现任何密码明文。
    /// </summary>
    public sealed class AuthException : Exception
    {
        /// <summary>稳定错误码，取值见 <see cref="AuthErrorCode"/>。</summary>
        public string Code { get; }

        /// <summary>业务标识（如账号、用户标识），便于按对象追查定位。</summary>
        public string? Subject { get; }

        /// <summary>
        /// 构造认证业务异常。
        /// </summary>
        /// <param name="code">稳定错误码，取值见 <see cref="AuthErrorCode"/>。</param>
        /// <param name="message">面向操作人员的可读中文说明，禁止包含密码明文。</param>
        /// <param name="subject">业务标识，可为空。</param>
        /// <param name="innerException">内部异常。</param>
        public AuthException(string code, string message, string? subject = null, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            Subject = subject;
        }
    }
}
