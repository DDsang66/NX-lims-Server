namespace NX_lims_Softlines_Command_System.src.Application.Interface.FiberTeamContext
{
    public interface IWordTemplateEngine
    {
        /// <summary>
        /// 替换书签文本。后面那几个集合/表都是可选的：
        /// <paramref name="redBookmarks"/> 标红；
        /// <paramref name="removeWhenEmpty"/> 值为空时删掉书签**前的文字**（所在表保留）；
        /// <paramref name="removeBlockWhenEmpty"/> 值为空时把书签所在的**整张表**删掉（整块书签用）；
        /// <paramref name="replaceParagraphText"/>
        /// 整段文字改写表（段落的完整文字 → 新文字），给模板里那些**没有书签的固定标签**用。
        /// 它在书签处理**之前**执行 —— 同段有书签时，先改写、后按书签的空值规则删。
        /// <paramref name="fadedBookmarks"/> 印淡灰（组头汇总行用），与红色同时命中时红优先。
        /// </summary>
        void ReplaceText(string filePath, Dictionary<string, string> bookmarkValues, HashSet<string>? redBookmarks = null, HashSet<string>? removeWhenEmpty = null, HashSet<string>? removeBlockWhenEmpty = null, IReadOnlyDictionary<string, string>? replaceParagraphText = null, HashSet<string>? fadedBookmarks = null);
        void InsertMicroscopeImages(string filePath, IEnumerable<string> fiberNames, string imageFolder);
    }
}
