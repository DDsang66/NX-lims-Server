using NX_lims_Softlines_Command_System.src.Application.Service.Realtime;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse;

namespace NX_lims_Softlines_Command_System.src.Application.Service.DataSheetContext
{
    /// <summary>
    /// 【迁移期兼容薄壳】DataSheet 链路的推送入口。
    ///
    /// 实际推送逻辑已移交通用 <see cref="ISseHub"/>（频道固定 <c>"datasheet"</c>），
    /// 本类只保留 <c>Guid checkListId</c> ↔ <c>string key</c> 的转换，
    /// 使 <c>DataSheetController</c> 的调用形态保持稳定。
    ///
    /// 原类内部持有的以下职责已迁出，不要再往本类里加回来：
    /// · 连接表与 dead 清理 → <see cref="ISseHub"/>；
    /// · 快照哈希计算（原硬编码 DataSheet 字段）→ <see cref="DataSheetProgressSnapshotProvider"/>；
    /// · 每秒轮询派发 → <see cref="ProgressPollingService"/>。
    ///
    /// 后续若所有调用方都改为直接使用 <see cref="ISseHub"/>，本类可整体删除。
    /// </summary>
    public class DataSheetProgressHub : ISingletonDependency
    {
        /// <summary>DataSheet 链路的频道名，必须与 Provider 中的 Channel 保持一致。</summary>
        private const string Channel = "datasheet";

        private readonly ISseHub _sseHub;

        /// <summary>
        /// 构造兼容薄壳。
        /// </summary>
        /// <param name="sseHub">通用推送枢纽。</param>
        /// <exception cref="ArgumentNullException">sseHub 为 null。</exception>
        public DataSheetProgressHub(ISseHub sseHub)
        {
            _sseHub = sseHub ?? throw new ArgumentNullException(nameof(sseHub));
        }

        /// <summary>
        /// 注册一个清单的进度监听连接。
        /// </summary>
        /// <param name="checkListId">清单 ID。</param>
        /// <param name="client">SSE 连接。</param>
        /// <param name="ct">取消令牌。</param>
        public Task RegisterAsync(Guid checkListId, ISseClient client, CancellationToken ct = default)
            => _sseHub.RegisterAsync(Channel, checkListId.ToString(), client, ct);

        /// <summary>
        /// 取消注册。
        /// </summary>
        /// <param name="checkListId">清单 ID。</param>
        /// <param name="client">要移除的连接。</param>
        public Task UnregisterAsync(Guid checkListId, ISseClient client)
            => _sseHub.UnregisterAsync(Channel, checkListId.ToString(), client);

        /// <summary>
        /// 取当前有活跃订阅的清单 ID 集合（诊断用途）。
        /// </summary>
        /// <returns>活跃清单 ID 列表；无订阅时为空集合。</returns>
        public IReadOnlyCollection<Guid> GetActiveCheckListIds()
            => _sseHub.GetActiveKeys(Channel)
                     .Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty)
                     .Where(id => id != Guid.Empty)
                     .ToList();
    }
}
