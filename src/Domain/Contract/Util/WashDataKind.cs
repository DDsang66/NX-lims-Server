namespace NX_lims_Softlines_Command_System.src.Domain.Contract.Util
{
    /// <summary>
    /// 洗涤类数据的种类。
    ///
    /// 用途：样本数据（SampleMap）与洗后数据（AfterWashMap）原本是两套几乎逐行重复的
    /// 展开方法与书签映射方法，仅在「书签前缀」与「模板容量字段」上有差异。
    /// 引入本枚举后由统一方法按种类分支，避免两份同构算法日后口径分叉。
    /// </summary>
    public enum WashDataKind
    {
        /// <summary>
        /// 样本数据：书签前缀 <c>Sample_data_</c>，容量取
        /// <c>TemplateStructure.SampleDataAreaCount</c>。
        /// </summary>
        Sample = 0,

        /// <summary>
        /// 洗后数据：书签前缀 <c>After_wash_data_</c>，容量取
        /// <c>TemplateStructure.AfterWashDataCount</c>。
        /// </summary>
        AfterWash = 1
    }
}
