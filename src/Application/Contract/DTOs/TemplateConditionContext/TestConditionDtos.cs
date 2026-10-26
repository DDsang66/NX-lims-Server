using System;
using System.Collections.Generic;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.TemplateContext;

namespace NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext
{
    /// <summary>
    /// testcondition 合成失败异常。
    ///
    /// 所有「条件不齐备 / 模板缺失」的失败都以本类型抛出，目的是让上层
    /// （当前是日志，后续是审计模块）能拿到结构化字段落表，
    /// 便于操作人员与运维按 Code + CheckListId + TestPoint 快速定位原因。
    /// </summary>
    public sealed class TestConditionCompositionException : Exception
    {
        /// <summary>稳定错误码，取值来自 <see cref="TestConditionErrorCodes"/>，供审计模块分类统计。</summary>
        public string Code { get; }

        /// <summary>触发异常的 checklist 标识；非 checklist 场景（datasheet 侧）为 null。</summary>
        public Guid? CheckListId { get; }

        /// <summary>触发异常的测点标识；与测点无关的失败（如标准库查询失败）为 null。</summary>
        public string? TestPoint { get; }

        /// <summary>补充说明，直接面向操作人员阅读（例如缺失了哪些前置条件、应如何处理）。</summary>
        public string? Detail { get; }

        /// <summary>
        /// 构造一个 testcondition 合成失败异常。
        /// </summary>
        /// <param name="code">稳定错误码，见 <see cref="TestConditionErrorCodes"/>。</param>
        /// <param name="message">面向操作人员的失败描述，应包含可读的定位信息。</param>
        /// <param name="checkListId">关联的 checklist 标识，可空。</param>
        /// <param name="testPoint">关联的测点标识，可空。</param>
        /// <param name="detail">修复指引等补充说明，可空。</param>
        /// <param name="inner">内部异常，可空。</param>
        public TestConditionCompositionException(
            string code,
            string message,
            Guid? checkListId = null,
            string? testPoint = null,
            string? detail = null,
            Exception? inner = null) : base(message, inner)
        {
            Code = code;
            CheckListId = checkListId;
            TestPoint = testPoint;
            Detail = detail;
        }
    }

    /// <summary>
    /// testcondition 合成失败的错误码常量。
    /// 审计模块可按此分类统计；新增错误码时请同步更新本类与使用方 README 的错误码表。
    /// </summary>
    public static class TestConditionErrorCodes
    {
        /// <summary>未找到该 checklist 的条件池（ConditionPool 为空或不存在）。</summary>
        public const string ConditionPoolMissing = "TC001";

        /// <summary>该测点未在条件池中命中（ConditionPool.TestPoints 不包含该测点）。</summary>
        public const string ConditionPoolNotMatchedForTestPoint = "TC002";

        /// <summary>该检测项缺少 TestPointParams（参数未生成或未落库）。</summary>
        public const string TestPointParamsMissing = "TC003";

        /// <summary>模板索引匹配未命中任何模板。</summary>
        public const string TemplateNotMatched = "TC004";

        /// <summary>模板缺少与该条件对应的文本模板（FindTextTemplate 返回 null）。</summary>
        public const string TemplateTextTemplateMissing = "TC005";

        /// <summary>模板 URL 为空，无法定位模板文件。</summary>
        public const string TemplateUrlEmpty = "TC006";

        /// <summary>
        /// dto 条目与清单条目无法唯一对齐（前端回传的标识与数据库不一致）。
        /// 典型场景：前端把 TestItem 从 ID 替换成展示名称且做了加工，导致无法按 ID 精确命中。
        /// </summary>
        public const string DtoItemNotAligned = "TC007";
    }

    /// <summary>
    /// 单个检测项合成后的 testcondition 结果（一个检测项可能对应 N 个参数分包）。
    /// </summary>
    /// <param name="Text">已按 CRLF 分行、并完成组内去重的最终单元格文本。</param>
    /// <param name="Lines">去重后的分行文本，供调用方判断行数或做二次处理。</param>
    /// <param name="PackageCount">去重前的分包总数（等于 PackByParamSet 的结果数），用于诊断。</param>
    public sealed record TestConditionText(string Text, IReadOnlyList<string> Lines, int PackageCount);

    /// <summary>
    /// 单个分包的 testcondition 合成结果。
    /// 附带命中的模板与运行时条件，便于调用方复用（例如回写 TemplateUrl、排查索引匹配问题）。
    /// </summary>
    /// <param name="Text">填充完成的 testcondition 文本。</param>
    /// <param name="Template">本次命中的模板实体。</param>
    /// <param name="RuntimeConditions">用于模板索引匹配的运行时条件字典。</param>
    public sealed record TestConditionComposition(
        string Text,
        Template Template,
        IReadOnlyDictionary<string, object?> RuntimeConditions);
}
