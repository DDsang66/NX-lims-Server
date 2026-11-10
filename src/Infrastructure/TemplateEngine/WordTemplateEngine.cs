using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Application.Interface.FiberTeamContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.TemplateEngine
{
    /// <summary>
    /// Word 模板引擎
    /// 仅封装底层操作功能，不涉及业务逻辑
    /// </summary>
    public class WordTemplateEngine : IWordTemplateEngine, IScopedDependency
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        public WordTemplateEngine()
        {
        }

        /// <summary>
        /// 根据书签替换文本（支持正文、页眉、页脚）
        /// </summary>
        /// <param name="filePath">Word文档路径</param>
        /// <param name="bookmarkValues">书签名-值字典</param>
        /// <param name="redBookmarks">标红的书签名</param>
        /// <param name="removeWhenEmpty">值为空时**删掉书签前的文字**（标签类的书签用；所在表格保留）</param>
        /// <param name="removeBlockWhenEmpty">
        /// 值为空时**把书签所在的整张表删掉**（整块类的书签用，如 Conclusion / Remark）。
        /// 与 <paramref name="removeWhenEmpty"/> 是两件事，别合并 —— 后者的典型用户 Recommend
        /// 就住在结果表里，那张表**删不得**。见 <see cref="RemoveTableForBookmark"/>。
        /// </param>
        /// <param name="replaceParagraphText">
        /// 整段文字改写表（段落的完整文字 → 新文字）。给模板里那些**连书签都没有的固定标签**
        /// 用 —— 它们换不了"值"，只能整段换掉。传 null（缺省）则一个段落都不动。
        /// 见 <see cref="ReplaceParagraphTexts"/>。
        /// </param>
        /// <param name="fadedBookmarks">
        /// 印**淡灰**的书签名（成分报告里组头汇总行那种"不是一条真实成分"的行，
        /// 靠颜色与下面的成分子行区分开）。与 <paramref name="redBookmarks"/> 是两套颜色，
        /// 同时命中时**红优先**（红色是"平行样差异过大"的警示，不能被淡灰吃掉）。
        /// 参数排在最后，是为了不挪动既有调用点的位置参数。
        /// </param>
        public void ReplaceText(string filePath, Dictionary<string, string> bookmarkValues, HashSet<string>? redBookmarks = null, HashSet<string>? removeWhenEmpty = null, HashSet<string>? removeBlockWhenEmpty = null, IReadOnlyDictionary<string, string>? replaceParagraphText = null, HashSet<string>? fadedBookmarks = null)
        {
            if (bookmarkValues == null || !bookmarkValues.Any()) return;

            using (WordprocessingDocument doc = WordprocessingDocument.Open(filePath, true))
            {
                // 整段改写**必须**排在书签处理之前：Recommendation 的标签与 Recommend 书签同段，
                // 推荐标签为空时 RemoveTextBeforeBookmark 会把标签一起删掉 —— 先改后删，
                // 与"英文标签跟着消失"的既有行为逐字一致；反过来先删改不到，就会留下一行中文空标签。
                if (replaceParagraphText != null && replaceParagraphText.Count > 0)
                {
                    ReplaceParagraphTexts(doc.MainDocumentPart!, replaceParagraphText);
                    doc.MainDocumentPart.Document!.Save();

                    foreach (var headerPart in doc.MainDocumentPart.HeaderParts)
                    {
                        ReplaceParagraphTexts(headerPart, replaceParagraphText);
                        headerPart.Header?.Save();
                    }

                    foreach (var footerPart in doc.MainDocumentPart.FooterParts)
                    {
                        ReplaceParagraphTexts(footerPart, replaceParagraphText);
                        footerPart.Footer?.Save();
                    }
                }

                // 正文部件
                GrowIndexedRows(doc.MainDocumentPart!, bookmarkValues);
                ReplaceBookmarksInPart(doc.MainDocumentPart!, bookmarkValues, redBookmarks, removeWhenEmpty, removeBlockWhenEmpty, fadedBookmarks);
                doc.MainDocumentPart.Document!.Save();

                // 页眉
                foreach (var headerPart in doc.MainDocumentPart!.HeaderParts)
                {
                    GrowIndexedRows(headerPart, bookmarkValues);
                    ReplaceBookmarksInPart(headerPart, bookmarkValues, redBookmarks, removeWhenEmpty, removeBlockWhenEmpty, fadedBookmarks);
                    headerPart.Header?.Save();
                }

                // 页脚
                foreach (var footerPart in doc.MainDocumentPart.FooterParts)
                {
                    GrowIndexedRows(footerPart, bookmarkValues);
                    ReplaceBookmarksInPart(footerPart, bookmarkValues, redBookmarks, removeWhenEmpty, removeBlockWhenEmpty, fadedBookmarks);
                    footerPart.Footer?.Save();
                }
            }
        }

        /// <summary>
        /// 按**整段文字**改写固定标签。给那些在模板里是纯文本、连书签都没有的常量用
        /// （成分报告上的 `Test Result:` / `Recommendation:` 等五个）。
        /// </summary>
        /// <remarks>
        /// **匹配的是"段内全部文字串起来后 trim"**，不是"某个 `<w:t>` 等于" ——
        /// 同一个标签在模板里可能被拆成好几个 run（`Based on moisture regain weight:`
        /// 就是 `"Based on "` / `"moisture"` / `" regain weight:"` 三个），
        /// 只比单个 `<w:t>` 永远匹配不上。
        ///
        /// 改写**就地**进行：第一个 `<w:t>` 换成新文字，同段其余 `<w:t>` 清空。
        /// 样式（粗体、字号、对齐）挂在 `<w:rPr>` 上、不在 `<w:t>` 上，保留原 Run 即保留版式；
        /// 而**清空 Text 而不是删 Run**，是因为同段还可能有书签、域、制表位等非文字元素，
        /// 删 Run 会把它们一并带走。
        /// </remarks>
        private static void ReplaceParagraphTexts(OpenXmlPart part, IReadOnlyDictionary<string, string> replaceParagraphText)
        {
            var paragraphs = part.RootElement?.Descendants<Paragraph>().ToList();
            if (paragraphs == null) return;

            foreach (var paragraph in paragraphs)
            {
                // 段内所有 <w:t>
                var texts = paragraph.Descendants<Text>().ToList();
                if (texts.Count == 0) continue;

                // 串起来
                var paragraphText = string.Concat(texts.Select(t => t.Text));
                if (!replaceParagraphText.TryGetValue(paragraphText.Trim(), out var replacement)) continue;

                // 就地改第一个
                texts[0].Text = replacement;
                texts[0].Space = SpaceProcessingModeValues.Preserve;

                // 其余清空
                for (int i = 1; i < texts.Count; i++)
                    texts[i].Text = string.Empty;
            }
        }

        /// <summary>`前缀 + 行号` 的书签名（`Section_12` / `Component14`）：前缀非空，行号取末尾连续数字。</summary>
        private static readonly Regex IndexedBookmark = new(@"^(?<prefix>.+?)(?<n>\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// 模板行数不够时，把带序号的表格行**就地克隆**到够用。
        /// </summary>
        /// <remarks>
        /// 模板的行数是写死的（结果表 `Section_1..Section_12`），适配器生成的行号却**没有上限**
        /// （见 <c>ExpandMultiFiberRowUnits</c>）。超出的行在模板里找不到书签，
        /// 就被 <see cref="ReplaceBookmarksInPart"/> 的 `bookmarkValues.ContainsKey` 筛掉 ——
        /// 不报错、不记日志，报告上直接少几行。所以在替换**之前**先补行。
        ///
        /// 只在真超了才动手：没有任何书签家族需要扩容时，这个方法一个字节都不改，
        /// 既有报告因此逐字不变 —— 尾部那一串空行是现在的常态外观，不归这里管。
        ///
        /// 同一行常挂着好几个前缀（结果表最后一行的 `Section_12`/`Sum_12`/`Avg_12`…），
        /// **一行只克隆一次**，克隆份数取该行所有前缀里 `需要 - 已有` 的最大值。
        /// </remarks>
        private static void GrowIndexedRows(OpenXmlPart part, Dictionary<string, string> bookmarkValues)
        {
            var root = part.RootElement;
            if (root == null) return;

            // 模板里现有的行号：前缀 → 已有哪几个序号
            var present = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var start in root.Descendants<BookmarkStart>())
            {
                if (!TrySplitIndex(start.Name?.Value, out var prefix, out var n)) continue;
                if (!present.TryGetValue(prefix, out var set)) present[prefix] = set = new HashSet<int>();
                set.Add(n);
            }
            if (present.Count == 0) return;

            // 数据需要的行号：同样按前缀取最大值
            var needed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var key in bookmarkValues.Keys)
            {
                if (!TrySplitIndex(key, out var prefix, out var n)) continue;
                if (!needed.TryGetValue(prefix, out var cur) || n > cur) needed[prefix] = n;
            }

            var rows = new Dictionary<TableRow, RowGrowth>();
            foreach (var pair in needed)
            {
                var prefix = pair.Key;
                if (!present.TryGetValue(prefix, out var have)) continue;

                // 只认"从 1 起连续到 max"的家族，那才是行号书签的形状。
                var haveMax = have.Max();
                if (!have.Contains(1) || have.Count != haveMax) continue;
                if (pair.Value <= haveMax) continue;

                // 克隆源 = 这个家族的**最后一行**
                var source = root.Descendants<BookmarkStart>()
                    .FirstOrDefault(b => b.Name?.Value == prefix + haveMax);
                var row = source?.Ancestors<TableRow>().FirstOrDefault();
                if (row == null) continue;   // 不在表里（页眉页脚的常量书签）→ 无处可克隆

                if (!rows.TryGetValue(row, out var growth)) rows[row] = growth = new RowGrowth();
                growth.Prefixes.Add(prefix);
                growth.HaveMax[prefix] = haveMax;
                if (pair.Value - haveMax > growth.Extra) growth.Extra = pair.Value - haveMax;
            }

            if (rows.Count == 0) return;

            var nextId = MaxBookmarkId(root);

            foreach (var pair in rows)
            {
                var row = pair.Key;
                var growth = pair.Value;

                // 逐份插在克隆源之后（而不是表尾），行号的先后顺序才和版面一致
                OpenXmlElement anchor = row;
                for (int k = 1; k <= growth.Extra; k++)
                {
                    var clone = (TableRow)row.CloneNode(true);
                    if (!RenumberClonedRow(clone, growth, k, ref nextId)) continue;
                    anchor.InsertAfterSelf(clone);
                    anchor = clone;
                }
            }
        }

        /// <summary>
        /// 给克隆出来的行重编书签。
        /// </summary>
        /// <remarks>
        /// ⚠️ **Name 与 Id 必须一起改**：<see cref="ReplaceBookmarksInPart"/> 是拿 `BookmarkStart.Id`
        /// 去找配对的 `BookmarkEnd` 的（取文档顺序第一个）。只改名字的话，克隆行里那些
        /// **同名同 Id** 的书签会让 <see cref="GetContentBetweenBookmarks"/> 把区间配到错误的位置。
        ///
        /// 序号这样算：`前缀{该家族已有最大序号 + k}` —— 第 k 份克隆接着最后一行往下排。
        /// 行内的其他书签（`_Hlk`、`GBR`、`Recommend` 之类）一个字都不动。
        ///
        /// 返回 false = 这一行一份都不克隆：行里有配对不上的 start/end（跨行的书签），
        /// 克隆只会产出半拉子书签。宁可维持现状（那几行数据仍旧印不出来），也不要把书签表弄脏。
        /// </remarks>
        private static bool RenumberClonedRow(TableRow clone, RowGrowth growth, int offset, ref int nextId)
        {
            var starts = clone.Descendants<BookmarkStart>().ToList();
            var ends = clone.Descendants<BookmarkEnd>().ToList();

            var endById = new Dictionary<string, BookmarkEnd>(StringComparer.Ordinal);
            foreach (var end in ends)
                if (end.Id?.Value is string id) endById[id] = end;

            var startIds = starts.Select(b => b.Id?.Value).Where(id => id != null).ToHashSet(StringComparer.Ordinal);
            if (!startIds.SetEquals(endById.Keys)) return false;

            foreach (var start in starts)
            {
                if (!TrySplitIndex(start.Name?.Value, out var prefix, out _)) continue;
                if (!growth.Prefixes.Contains(prefix)) continue;
                if (start.Id?.Value is not string oldId) continue;

                var newName = prefix + (growth.HaveMax[prefix] + offset);

                // Name 挂在 BookmarkStart 上，BookmarkEnd 只有 Id
                start.Name = newName;

                var freshId = (++nextId).ToString();
                start.Id = freshId;
                endById[oldId].Id = freshId;
            }

            return true;
        }

        /// <summary>部件内现有的最大书签 Id；新的从它 +1 往上发。</summary>
        private static int MaxBookmarkId(OpenXmlElement root)
        {
            var max = 0;
            foreach (var id in root.Descendants<BookmarkStart>().Select(b => b.Id?.Value)
                .Concat(root.Descendants<BookmarkEnd>().Select(b => b.Id?.Value)))
            {
                if (int.TryParse(id, out var value) && value > max) max = value;
            }

            return max;
        }

        /// <summary>
        /// 拆 `前缀 + 行号` 的书签名。拆不出来（`GBR`、`Recommend`）或行号大到 int 装不下的一律返回 false。
        /// </summary>
        private static bool TrySplitIndex(string? name, out string prefix, out int index)
        {
            prefix = string.Empty;
            index = 0;
            if (string.IsNullOrEmpty(name)) return false;

            var match = IndexedBookmark.Match(name);
            if (!match.Success || !int.TryParse(match.Groups["n"].Value, out index)) return false;

            prefix = match.Groups["prefix"].Value;
            return true;
        }

        /// <summary>一行要克隆几份，以及这行上挂着哪些要重编序号的家族。</summary>
        private sealed class RowGrowth
        {
            public HashSet<string> Prefixes { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, int> HaveMax { get; } = new(StringComparer.Ordinal);
            public int Extra { get; set; }
        }

        /// <summary>
        /// 在指定部件中替换书签
        /// 优先在原有 Run/Text 上就地替换以保留样式；若不存在则寻找局部最近的 RunProperties 并克隆；最后才插入无样式 Run。
        /// </summary>
        private void ReplaceBookmarksInPart(OpenXmlPart part, Dictionary<string, string> bookmarkValues, HashSet<string>? redBookmarks, HashSet<string>? removeWhenEmpty, HashSet<string>? removeBlockWhenEmpty, HashSet<string>? fadedBookmarks = null)
        {
            var bookmarks = part.RootElement!.Descendants<BookmarkStart>()
                .Where(b => bookmarkValues.ContainsKey(b.Name!))
                .ToList();

            foreach (var bookmark in bookmarks)
            {
                // 找到对应的 BookmarkEnd
                var bookmarkEnd = part.RootElement.Descendants<BookmarkEnd>()
                    .FirstOrDefault(be => be.Id?.Value == bookmark.Id?.Value);

                if (bookmarkEnd == null) continue;

                // 整块删：书签值为空且被点名 → 把书签所在的**整张表**拿掉（Conclusion / Remark 这类整块）。
                // 必须 continue —— 表都摘掉了，后面的"就地替换 / 找最近 RunProperties"就没有意义了。
                if (removeBlockWhenEmpty != null && removeBlockWhenEmpty.Contains(bookmark.Name!)
                    && string.IsNullOrEmpty(bookmarkValues[bookmark.Name]))
                {
                    RemoveTableForBookmark(bookmark);
                    continue;
                }

                // 若书签在 removeWhenEmpty 中且替换值为空，删除书签前的文本
                if (removeWhenEmpty != null && removeWhenEmpty.Contains(bookmark.Name!)
                    && string.IsNullOrEmpty(bookmarkValues[bookmark.Name]))
                {
                    RemoveTextBeforeBookmark(bookmark);
                }

                // 获取书签之间的所有元素（同一父级序列）
                var contentElements = GetContentBetweenBookmarks(part, bookmark, bookmarkEnd);

                // 优先在原有 Run 的 Text 上就地替换（保留 RunProperties）
                var existingRunWithText = contentElements.OfType<Run>()
                    .FirstOrDefault(r => r.Elements<Text>().Any());

                if (existingRunWithText != null)
                {
                    // 清空已有 text 元素，用 InsertTextWithLineBreaks 写入（支持 \n 换行）
                    foreach (var t in existingRunWithText.Elements<Text>().ToList())
                        t.Remove();
                    TextRunHelper.InsertTextWithLineBreaks(bookmarkValues[bookmark.Name], existingRunWithText);

                    // 上色：先淡灰、后标红 —— 两者同时命中时红优先（红是警示，不能被灰吃掉）
                    if (fadedBookmarks != null && fadedBookmarks.Contains(bookmark.Name))
                        ApplyColor(existingRunWithText, FadedColorHex);
                    if (redBookmarks != null && redBookmarks.Contains(bookmark.Name))
                        ApplyColor(existingRunWithText, RedColorHex);

                    // 删除书签范围内除保留的 run 之外的其他元素
                    foreach (var elem in contentElements.ToList())
                    {
                        if (!object.ReferenceEquals(elem, existingRunWithText))
                        {
                            elem.Remove();
                        }
                    }

                    continue;
                }

                // 如果没有就地可替换的 Run/Text，尝试寻找最近的 RunProperties（段落优先，单元格次之）
                var nearestRunProps = FindNearestRunProperties(bookmark);

                if (nearestRunProps != null)
                {
                    var newRun = new Run(nearestRunProps.CloneNode(true) as RunProperties);
                    InsertRunAfterBookmark(bookmark, newRun);
                    TextRunHelper.InsertTextWithLineBreaks(bookmarkValues[bookmark.Name], newRun);
                    if (fadedBookmarks != null && fadedBookmarks.Contains(bookmark.Name))
                        ApplyColor(newRun, FadedColorHex);
                    if (redBookmarks != null && redBookmarks.Contains(bookmark.Name))
                        ApplyColor(newRun, RedColorHex);
                }
                else
                {
                    // 兜底：插入无样式的 Run（将使用 Word 的默认样式）
                    var newRun = new Run();
                    InsertRunAfterBookmark(bookmark, newRun);
                    TextRunHelper.InsertTextWithLineBreaks(bookmarkValues[bookmark.Name], newRun);
                    if (fadedBookmarks != null && fadedBookmarks.Contains(bookmark.Name))
                        ApplyColor(newRun, FadedColorHex);
                    if (redBookmarks != null && redBookmarks.Contains(bookmark.Name))
                        ApplyColor(newRun, RedColorHex);
                }
            }
        }

        /// <summary>标红：平行样差异过大。</summary>
        private const string RedColorHex = "FF0000";

        /// <summary>
        /// 淡灰：组头汇总行（`E/T` 这种缩写串）整行印灰，与真实成分子行区分。
        /// 取 50% 灰（808080）—— 再淡在 8pt 下就快看不清了。
        /// </summary>
        private const string FadedColorHex = "808080";

        /// <summary>
        /// 将 Run 的字体颜色设为指定色值（六位十六进制，不带 #）
        /// </summary>
        private static void ApplyColor(Run run, string colorHex)
        {
            run.RunProperties ??= new RunProperties();
            var color = run.RunProperties.Elements<Color>().FirstOrDefault();
            if (color != null)
                color.Val = colorHex;
            else
                run.RunProperties.Append(new Color { Val = colorHex });
        }

        /// <summary>
        /// 在书签位置后插入 Run，确保插入点有效（书签 Parent 可能是 Run、Paragraph 等）
        /// </summary>
        private void InsertRunAfterBookmark(BookmarkStart bookmark, Run run)
        {
            OpenXmlElement? parent = bookmark.Parent;
            if (parent == null)
            {
                // 兜底：将 run 插入到书签的祖先段落末尾
                var para = bookmark.Ancestors<Paragraph>().FirstOrDefault();
                if (para != null) para.Append(run);
                return;
            }

            try
            {
                parent.InsertAfter(run, bookmark);
            }
            catch
            {
                // 若直接插入失败，退回到父段落末尾
                var para = bookmark.Ancestors<Paragraph>().FirstOrDefault();
                if (para != null) para.Append(run);
            }
        }

        /// <summary>
        /// 在书签与对应 BookmarkEnd 之间收集元素（基于 NextSibling 遍历，适用于位于同一父级的情况）
        /// </summary>
        private List<OpenXmlElement> GetContentBetweenBookmarks(OpenXmlPart part, BookmarkStart bookmark, BookmarkEnd bookmarkEnd)
        {
            var result = new List<OpenXmlElement>();
            var current = bookmark.NextSibling();

            while (current != null && current != bookmarkEnd)
            {
                result.Add(current);
                current = current.NextSibling();
            }

            return result;
        }

        /// <summary>
        /// 清除书签之间的内容（保留书签标记）
        /// </summary>
        private void ClearContentBetweenBookmarks(OpenXmlPart part, BookmarkStart bookmark, BookmarkEnd bookmarkEnd)
        {
            var current = bookmark.NextSibling();

            while (current != null && current != bookmarkEnd)
            {
                var next = current.NextSibling();
                current.Remove();
                current = next;
            }
        }

        /// <summary>
        /// 查找书签附近最近的 RunProperties：优先同段落相邻 Run，若没有则在同单元格内查找。
        /// 不再退回到部件任意 Run，以避免把不同位置的样式统一。
        /// </summary>
        private RunProperties? FindNearestRunProperties(BookmarkStart bookmark)
        {
            // 1. 同段落内查找相邻 RunProperties（向前向后）
            var para = bookmark.Ancestors<Paragraph>().FirstOrDefault();
            if (para != null)
            {
                var children = para.ChildElements.ToList();
                int idx = children.IndexOf(bookmark);
                if (idx >= 0)
                {
                    for (int i = idx - 1; i >= 0; i--)
                    {
                        if (children[i] is Run r && r.RunProperties != null)
                        {
                            return r.RunProperties.CloneNode(true) as RunProperties;
                        }
                    }

                    for (int i = idx + 1; i < children.Count; i++)
                    {
                        if (children[i] is Run r && r.RunProperties != null)
                        {
                            return r.RunProperties.CloneNode(true) as RunProperties;
                        }
                    }
                }
            }

            // 2. 在同一 TableCell 范围内查找任一带格式的 Run
            var cell = bookmark.Ancestors<TableCell>().FirstOrDefault();
            if (cell != null)
            {
                var runInCell = cell.Descendants<Run>().FirstOrDefault(r => r.RunProperties != null);
                if (runInCell != null)
                {
                    return runInCell.RunProperties.CloneNode(true) as RunProperties;
                }
            }

            return null;
        }

        /// <summary>
        /// 替换书签内的内容（备用方法：在同段落内删除并插入新 Run）
        /// </summary>
        private void ReplaceBookmarkContent(BookmarkStart start, BookmarkEnd end, string newText)
        {
            var parentPara = start.Ancestors<Paragraph>().FirstOrDefault();
            if (parentPara == null) return;

            var elementsBetween = GetElementsBetween(start, end).ToList();

            foreach (var elem in elementsBetween)
            {
                elem.Remove();
            }

            var newRun = new Run(
                new RunProperties(),
                new Text(newText) { Space = SpaceProcessingModeValues.Preserve }
            );

            start.InsertAfterSelf(newRun);
        }

        /// <summary>
        /// 删除书签前（同一父元素内）的文本 — 用于 removeWhenEmpty
        /// </summary>
        private void RemoveTextBeforeBookmark(BookmarkStart bookmark)
        {
            var parent = bookmark.Parent;
            if (parent == null) return;

            // 收集 BookmarkStart 之前的所有 Text 元素
            var elementsBefore = parent.ChildElements
                .TakeWhile(e => e != bookmark)
                .OfType<Run>()
                .SelectMany(r => r.Elements<Text>())
                .ToList();

            foreach (var text in elementsBefore)
                text.Remove();

            // 也删除 BookmarkStart 之前独立的 Run（里面可能只有 Text，已被上面清空）
            var runsBefore = parent.ChildElements
                .TakeWhile(e => e != bookmark)
                .OfType<Run>()
                .Where(r => !r.Elements<Text>().Any())
                .ToList();

            foreach (var run in runsBefore)
                run.Remove();
        }

        /// <summary>
        /// 把书签所在的**整张表**删掉 —— 用于 removeBlockWhenEmpty。
        ///
        /// <para>
        /// 为什么非删表不可：Conclusion / Remark 这两块在模板里就是两张**无边框**的表
        /// （`tblBorders` 全 none），标签 "Conclusion:" / "Remark:" 是表里的**普通文字**，
        /// 不是书签。所以只把书签值清空、或者只按 <see cref="RemoveTextBeforeBookmark"/>
        /// 删掉标签，都会剩下一张空表占着版面 —— 看起来就是报告里凭空多一块空白。
        /// </para>
        ///
        /// <para>
        /// 连带的空段落：两张表前后各有一个 9pt 的空段落当间距。表没了、段落还留着，
        /// 就是 ~18pt 的空白。所以表被删掉时，把它**后面**紧邻的空段落一起收掉，
        /// 收到最后一个非空段落为止。判据见 <see cref="IsTrulyEmpty"/>（从严）。
        /// </para>
        ///
        /// <para>
        /// ⚠️ 收空段落**只在表前面也是空段落时才做**：这样保证收完至少还剩一个空段落，
        /// 相邻两张表不会因此贴到一起 —— Word 会把紧挨着的两张表合并成一张，那是另一个 bug。
        /// </para>
        /// </summary>
        private void RemoveTableForBookmark(BookmarkStart bookmark)
        {
            var table = bookmark.Ancestors<Table>().FirstOrDefault();
            if (table == null) return;   // 书签不在表里：什么都不做，绝不误删正文

            // 表前的「真空段 / 纯分页段」—— 那个分页符是**为这张表**开的页，
            // 表删了它必须跟着走，否则报告尾部会多出一张空白页。
            // 先收集、后动手；nearest-first，与下面的尾部空段清理互不干扰。
            var leadingRun = new List<Paragraph>();
            for (var cursor = table.PreviousSibling();
                 cursor is Paragraph leading && (IsTrulyEmpty(leading) || IsPageBreakOnlyParagraph(leading));
                 cursor = cursor.PreviousSibling())
            {
                leadingRun.Add(leading);
            }

            bool hasPageBreak = leadingRun.Any(IsPageBreakOnlyParagraph);

            if (table.PreviousSibling() is Paragraph prev && IsTrulyEmpty(prev))
            {
                var next = table.NextSibling();
                while (next is Paragraph p && IsTrulyEmpty(p))
                {
                    var after = p.NextSibling();
                    p.Remove();
                    next = after;
                }
            }

            // 只有前导段里真有分页符时才收前导段 —— 没有分页符时（Conclusion / Remark）
            // 这段完全不执行，删除行为与改动前逐字相同。
            if (hasPageBreak)
            {
                int lastBreak = leadingRun.FindLastIndex(IsPageBreakOnlyParagraph);
                for (int i = 0; i <= lastBreak; i++)
                    leadingRun[i].Remove();

                // 前导段收光后若两张表贴到一起，Word 会把它们并成一张 —— 补一个空段隔开。
                var afterTable = table.NextSibling();
                if (table.PreviousSibling() is Table && afterTable is Table)
                    afterTable.InsertBeforeSelf(new Paragraph());
            }

            table.Remove();
        }

        /// <summary>
        /// 真空段落：没有非空白文字、没有书签、没有图/对象/换行/分页。
        /// 判据从严 —— 拿不准就当作"非空"，宁可不收段落，也不误删内容。
        /// </summary>
        private static bool IsTrulyEmpty(Paragraph p)
            => !p.Descendants<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))
            && !p.Descendants<BookmarkStart>().Any()
            && !p.Descendants().Any(e => e.LocalName is "drawing" or "pict" or "object" or "br");

        /// <summary>
        /// **纯分页段**：只承担分页，没有任何别的内容。
        /// 与 <see cref="IsTrulyEmpty"/> 刻意分开 —— 后者把带 <c>br</c> 的段落判为非空，
        /// 而这里要认的正是"除分页符外什么都没有"的那一段。同样从严：拿不准就不删。
        /// </summary>
        private static bool IsPageBreakOnlyParagraph(Paragraph p)
        {
            var breaks = p.Descendants<Break>().ToList();
            return breaks.Count > 0
                && breaks.All(b => b.Type?.Value == BreakValues.Page)
                && !p.Descendants<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))
                && !p.Descendants<BookmarkStart>().Any()
                && !p.Descendants<SectionProperties>().Any()
                && !p.Descendants().Any(e => e.LocalName is "drawing" or "pict" or "object");
        }

        /// <summary>
        /// 获取两个元素之间的所有元素（仅处理在同一父级内的情况）
        /// </summary>
        private IEnumerable<OpenXmlElement> GetElementsBetween(BookmarkStart start, BookmarkEnd end)
        {
            var parent = start.Parent;
            if (parent != end.Parent) yield break;

            bool started = false;
            foreach (var elem in parent!.ChildElements.ToList())
            {
                if (elem == start)
                {
                    started = true;
                    continue;
                }

                if (elem == end) yield break;

                if (started) yield return elem;
            }
        }

        /// <summary>
        /// 从数据库获取书签名和值（预留方法）
        /// </summary>
        private Dictionary<string, string> GetBookmarksFromDatabase()
        {
            // TODO: 实现数据库查询
            // 示例：
            // return dbContext.Bookmarks.ToDictionary(b => b.Name, b => b.Value);

            return new Dictionary<string, string>();
        }

        /// <summary>
        /// 图片替换书签位（预留）
        /// </summary>
        /// <param name="filePath">Word文档路径</param>
        /// <param name="bookmarkName">书签名</param>
        /// <param name="imageId">图片在文档中的rId（需外部先通过 AddImagePart 添加）</param>
        /// <param name="imageName">图片文件名（用于描述）</param>
        /// <param name="widthEmu">图片宽度（EMU），默认约6英寸</param>
        /// <param name="heightEmu">图片高度（EMU），默认约4英寸</param>
        public void ReplaceWithImage(string filePath, string bookmarkName,
            string imageId, string imageName,
            long widthEmu = 5486400, long heightEmu = 3657600)
        {
        }

        /// <summary>
        /// 对word插入新表（预留）
        /// </summary>
        /// <param name="doc">WordprocessingDocument</param>
        /// <param name="columns">列数</param>
        /// <param name="rows">行数</param>
        /// <param name="paragraphBookmark">书签名，表格插入到该书签所在段落后。为空则添加到body末尾</param>
        /// <returns>新创建的Table</returns>
        public Table AddNewTable(WordprocessingDocument doc, int columns, int rows,
            string? paragraphBookmark = null)
        {
            return null!;
        }

        /// <summary>
        /// 删除表格中的某一行（预留）
        /// </summary>
        /// <param name="table">目标表格</param>
        /// <param name="rowIndex">要删除的行索引（0-based）</param>
        public void RemoveRow(Table table, int rowIndex)
        {
            // 可能需要触发同一表格之中书签顺序的更新
        }

        /// <summary>
        /// 在 Microscopeview1~14 书签后按输入纤维顺序填入对应图片
        /// </summary>
        public void InsertMicroscopeImages(string filePath, IEnumerable<string> fiberNames, string imageFolder)
        {
            using (WordprocessingDocument doc = WordprocessingDocument.Open(filePath, true))
            {
                var mainPart = doc.MainDocumentPart!;
                var body = mainPart.Document.Body;

                var bookmarks = body.Descendants<BookmarkStart>()
                    .Where(b => b.Name != null && b.Name.Value != null && b.Name.Value.StartsWith("Microscopeview"))
                    .OrderBy(b => int.Parse(b.Name!.Value!.Replace("Microscopeview", "")))
                    .ToList();

                int slotIndex = 0;

                foreach (var fiberName in fiberNames)
                {
                    if (string.IsNullOrWhiteSpace(fiberName)) continue;

                    // 图库文件名走别名表（Spandex→Elastane 等，见 MicroscopeImageName）。
                    // 只用别名**找文件** —— 下面图注那行仍用录入名 fiberName，一字未改。
                    var imagePath = Path.Combine(imageFolder, $"{MicroscopeImageName.Resolve(fiberName)}.png");
                    if (!File.Exists(imagePath)) continue;

                    if (slotIndex >= bookmarks.Count) break;

                    var bookmark = bookmarks[slotIndex++];

                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    var mediaPart = mainPart.AddImagePart(ImagePartType.Png);
                    using var ms = new MemoryStream(imageBytes);
                    mediaPart.FeedData(ms);
                    string contentId = mainPart.GetIdOfPart(mediaPart);

                    var drawing = CreateImageDrawing(contentId, fiberName, (uint)slotIndex);

                    var paragraph = bookmark.Ancestors<Paragraph>().FirstOrDefault();
                    if (paragraph == null) continue;

                    // 获取书签段的 RunProperties 用于文字样式
                    var refRunProps = paragraph.Elements<Run>()
                        .Select(r => r.RunProperties)
                        .FirstOrDefault(rp => rp != null);

                    // 在书签所在段落后插入图片段+名称段
                    var imageParagraph = new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }
                        ),
                        new Run(
                            new RunProperties(new NoProof()),
                            drawing
                        )
                    );
                    var nameParagraph = new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }
                        ),
                        new Run(
                            refRunProps?.CloneNode(true) as RunProperties ?? new RunProperties(),
                            new Text(fiberName) { Space = SpaceProcessingModeValues.Preserve }
                        )
                    );
                    paragraph.Parent!.InsertAfter(nameParagraph, paragraph);
                    paragraph.Parent!.InsertAfter(imageParagraph, paragraph);
                }

                mainPart.Document.Save();
            }
        }

        private static Drawing CreateImageDrawing(string relationshipId, string imageName, uint id)
        {
            const long emuCm = 360000;
            long width = 2 * emuCm;
            long height = 135 * emuCm / 100;  // 1.35cm

            return new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = width, Cy = height },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = id, Name = imageName },
                    new DW.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks { NoChangeAspect = true }
                    ),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = id, Name = $"{imageName}.png" },
                                    new PIC.NonVisualPictureDrawingProperties()
                                ),
                                new PIC.BlipFill(
                                    new A.Blip { Embed = relationshipId },
                                    new A.Stretch(new A.FillRectangle())
                                ),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset { X = 0L, Y = 0L },
                                        new A.Extents { Cx = width, Cy = height }
                                    ),
                                    new A.PresetGeometry(new A.AdjustValueList())
                                    { Preset = A.ShapeTypeValues.Rectangle }
                                )
                            )
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                    )
                )
                {
                    DistanceFromTop = 0, DistanceFromBottom = 0,
                    DistanceFromLeft = 0, DistanceFromRight = 0
                }
            );
        }

        // 换页规则
        // 表格合并规则
        // 键入空白行
    }
}