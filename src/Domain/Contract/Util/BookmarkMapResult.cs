using System;
using System.Collections.Generic;

namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Util
{
    /// <summary>
    /// 书签映射的聚合结果。
    ///
    /// 把「书签名称 → 值」的映射与「是否需要复制整块 data 表」的判定绑在一起返回，
    /// 避免调用方分两次获取导致两次遍历结果不一致。
    /// </summary>
    public sealed class BookmarkMapResult
    {
        /// <summary>
        /// 书签名称到单元格值的映射。模板不具备该数据区时为空字典（非 null）。
        /// </summary>
        public IReadOnlyDictionary<string, string> Map { get; }

        /// <summary>
        /// 是否需要复制整块 data 表以扩展数据区。
        /// 判定口径：模板预留格数大于 0，且展开个数「严格大于」预留格数时才为 true
        /// （相等时刚好填满，不复制）。
        /// </summary>
        public bool IsAppend { get; }

        /// <summary>
        /// 构造书签映射结果。
        /// </summary>
        /// <param name="map">书签名称到值的映射，不可为 null。</param>
        /// <param name="isAppend">是否需要复制整块 data 表。</param>
        /// <exception cref="ArgumentNullException"><paramref name="map"/> 为 null 时抛出。</exception>
        public BookmarkMapResult(IReadOnlyDictionary<string, string> map, bool isAppend)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            IsAppend = isAppend;
        }

        /// <summary>
        /// 模板不具备该数据区时的空结果：空字典 + 不复制。
        /// </summary>
        public static BookmarkMapResult Empty { get; } =
            new BookmarkMapResult(new Dictionary<string, string>(), false);
    }
}
