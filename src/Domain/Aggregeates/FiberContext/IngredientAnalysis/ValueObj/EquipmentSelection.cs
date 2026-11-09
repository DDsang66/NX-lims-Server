namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.FiberContext.IngredientAnalysis.ValueObj
{
    /// <summary>
    /// 设备选型值对象
    /// 对应 Excel 中 L23/O23/R23/L24/O24 的设备自动选择逻辑
    /// </summary>
    public record EquipmentSelection
    {
        /// <summary>L23 — 显微镜（多组分必用）</summary>
        public string Microscope { get; init; } = string.Empty;

        /// <summary>O23 — 烘箱（多组分必用）</summary>
        public string Oven { get; init; } = string.Empty;

        /// <summary>R23 — 天平（多组分必用）</summary>
        public string Balance { get; init; } = string.Empty;

        /// <summary>L24 — 水浴（含涤纶/腈纶时选用）</summary>
        public string WaterBath { get; init; } = string.Empty;

        /// <summary>O24 — 振荡器（含锦纶/羊毛/丝绸或拆分列时选用）</summary>
        public string Shaker { get; init; } = string.Empty;

        /// <summary>
        /// 纤维素 / 再生纤维素父槽追加的第二台显微镜——
        /// 印在 Equipment 整串的**末位**，不是塞回 <see cref="Microscope"/> 里。
        /// </summary>
        public string MicroscopeCellulosic { get; init; } = string.Empty;

        public bool IsEmpty =>
            string.IsNullOrEmpty(Microscope)
            && string.IsNullOrEmpty(Oven)
            && string.IsNullOrEmpty(Balance)
            && string.IsNullOrEmpty(WaterBath)
            && string.IsNullOrEmpty(Shaker)
            && string.IsNullOrEmpty(MicroscopeCellulosic);
    }
}
