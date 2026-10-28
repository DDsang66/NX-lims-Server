namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Service.StandardContext
{
    /// <summary>
    /// 标准方法号解析器：把具体标准号归纳为「方法级键」，用于模板索引匹配。
    ///
    /// 解决的问题：模板索引用的是精确字符串匹配，而同一测试方法的各变体标准号写法不同，
    /// 例如 <c>ISO 12945-2</c> / <c>EN ISO 12945-2</c> / <c>BS EN ISO 12945-2:2010</c>
    /// 都应指向「Martindale 起球」这同一个模板。把它们统一解析为 <c>ISO 12945-2</c> 即可。
    ///
    /// 为什么不用标准族（StandardFamily）：族粒度是 <c>ISO 12945</c>，
    /// 会把方法一（<c>12945-1</c>，ICI 起球）与方法二（<c>12945-2</c>，Martindale 起球）合并，
    /// 反而破坏模板索引的唯一性。本解析器**保留方法号**，粒度落在「方法」这一级。
    /// </summary>
    public interface IStandardMethodCodeResolver
    {
        /// <summary>
        /// 解析标准号，得到方法级键。
        /// </summary>
        /// <param name="standardCode">数据库中的标准号原文，可为 null 或空白。</param>
        /// <returns>
        /// 方法级键（如 <c>ISO 12945-2</c>、<c>GB/T 12945-2</c>、<c>AATCC 124</c>）；
        /// 无法识别出任何编号时返回 <c>null</c>，调用方据此决定不注入该条件键。
        /// </returns>
        /// <remarks>
        /// 本方法为纯函数、无副作用、无外部依赖，可安全并发调用。
        /// 解析规则是**启发式**而非完备规则，无法覆盖的写法会退化为「原样保留」或返回 null，
        /// 不会抛异常——避免因单个标准号格式异常阻断整条生成链路。
        /// </remarks>
        string? Resolve(string? standardCode);
    }
}
