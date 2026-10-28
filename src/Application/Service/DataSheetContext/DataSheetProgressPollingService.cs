using NX_lims_Softlines_Command_System.src.Application.Service.Realtime;

namespace NX_lims_Softlines_Command_System.src.Application.Service.DataSheetContext
{
    /// <summary>
    /// 【已停用】原 DataSheet 专用进度轮询服务。
    ///
    /// 轮询职责已移交通用 <see cref="ProgressPollingService"/>：
    /// 后者通过 <c>IEnumerable&lt;IProgressSnapshotProvider&gt;</c> 自动纳入
    /// <see cref="DataSheetProgressSnapshotProvider"/>，本类不再承担任何逻辑。
    ///
    /// ⚠️ 本类**不再注册为 HostedService**（<c>Program.cs</c> 已改为注册 <see cref="ProgressPollingService"/>）。
    /// 之所以保留类型而不直接删除，是为了让「原类名」在搜索时仍能找到迁移线索；
    /// 后续清理轮可直接删除本文件。
    ///
    /// 若误将它重新注册为 HostedService，会与通用轮询服务形成**双轮询**，
    /// 导致重复推送与哈希竞争——不要这么做。
    /// </summary>
    [Obsolete("轮询职责已移交 Application.Service.Realtime.ProgressPollingService，本类不再使用，可在后续清理中删除。")]
    public class DataSheetProgressPollingService : BackgroundService
    {
        /// <summary>
        /// 空实现：真正的轮询在 <see cref="ProgressPollingService"/>。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }
}
