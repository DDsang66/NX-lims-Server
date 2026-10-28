using System.Security.Cryptography;
using System.Text;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.DataSheetConetxt;
using NX_lims_Softlines_Command_System.src.Application.Contract.Realtime;
using NX_lims_Softlines_Command_System.src.Application.Service.Realtime;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;

namespace NX_lims_Softlines_Command_System.src.Application.Service.DataSheetContext
{
    /// <summary>
    /// DataSheet 链路的进度快照提供者（频道 <c>"datasheet"</c>）。
    ///
    /// 它是通用推送机制与 DataSheet 业务之间**唯一**的适配点：把
    /// <see cref="DataSheetProgressService"/> 返回的 <see cref="DataSheetProgressDto"/>
    /// 包装成通用 <see cref="ProgressSnapshot"/>，并承担原本硬编码在
    /// <c>DataSheetProgressHub.ComputeHash</c> 里的哈希计算与终态判定。
    ///
    /// 生命周期为 Singleton：本类无状态，内部通过 <c>IServiceScopeFactory</c>
    /// 为每次取快照创建 scope，以获取 Scoped 的 <see cref="DataSheetProgressService"/>，
    /// 从而避免 captive dependency。
    /// </summary>
    public class DataSheetProgressSnapshotProvider : IProgressSnapshotProvider, ISingletonDependency
    {
        /// <summary>频道名。必须与 <c>DataSheetProgressHub</c> 内的常量一致，否则推送会落到不同连接集合。</summary>
        private const string ChannelName = "datasheet";

        private readonly ISseHub _sseHub;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DataSheetProgressSnapshotProvider> _logger;

        /// <summary>
        /// 构造 DataSheet 快照提供者。
        /// </summary>
        /// <param name="sseHub">推送枢纽，用于取活跃订阅 key。</param>
        /// <param name="scopeFactory">作用域工厂，用于解析 Scoped 的进度服务。</param>
        /// <param name="logger">日志。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        public DataSheetProgressSnapshotProvider(
            ISseHub sseHub,
            IServiceScopeFactory scopeFactory,
            ILogger<DataSheetProgressSnapshotProvider> logger)
        {
            _sseHub = sseHub ?? throw new ArgumentNullException(nameof(sseHub));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Channel => ChannelName;

        /// <inheritdoc />
        /// <remarks>
        /// 直接复用枢纽的连接表，因此天然满足「无订阅返回空集合」——
        /// 没人订阅时轮询服务会跳过取快照，不做任何 DB 查询。
        /// </remarks>
        public Task<IReadOnlyCollection<string>> GetActiveKeysAsync(CancellationToken ct)
            => Task.FromResult(_sseHub.GetActiveKeys(ChannelName));

        /// <inheritdoc />
        /// <remarks>
        /// key 是 <c>checkListId.ToString()</c>；无法解析时返回一个 Status 为空的快照而非抛异常，
        /// 让轮询循环能继续处理同频道其它 key（失败只影响该 key）。
        /// </remarks>
        public async Task<ProgressSnapshot> GetSnapshotAsync(string key, CancellationToken ct)
        {
            if (!Guid.TryParse(key, out var checkListGuid))
            {
                _logger.LogWarning("DataSheet 进度 key 不是合法 Guid，已跳过。Key={Key}", key);
                return new ProgressSnapshot
                {
                    Key = key,
                    Status = string.Empty,
                    Payload = new DataSheetProgressDto(),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            }

            using var scope = _scopeFactory.CreateScope();
            var progressService = scope.ServiceProvider.GetRequiredService<DataSheetProgressService>();

            var dto = await progressService.GetSnapshot(new CheckListId(checkListGuid), ct);

            return new ProgressSnapshot
            {
                Key = key,
                Status = dto.Status ?? string.Empty,
                // Fingerprint 只用于自描述哈希口径，不下发前端；与 ComputeHash 里的拼接顺序保持一致，
                // 便于排查「为什么这次算了新哈希」。
                Fingerprint = new Dictionary<string, string>
                {
                    ["Status"] = dto.Status ?? string.Empty,
                    ["Success"] = dto.Success.ToString(),
                    ["Failed"] = dto.Failed.ToString(),
                    ["Generating"] = dto.Generating.ToString(),
                    ["Pending"] = dto.Pending.ToString(),
                    ["MergedPdfUrl"] = dto.MergedPdfUrl ?? string.Empty
                },
                // ★ Payload 直接放既有 DTO 实例，保证前端收到的 JSON 结构与改造前完全一致。
                Payload = dto,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        /// <inheritdoc />
        /// <remarks>
        /// 严格复刻原 <c>DataSheetProgressPollingService.IsCompleted</c> 的口径：
        /// <c>Total &gt; 0</c> 且状态为 SUCCESS / FAILED / PARTIAL_FAILED。
        /// 注意 <c>DataSheetProgressService</c> 内部统计 Success 时实际用的是
        /// <c>DataSheetStatus.Created</c>，此处保持既有行为不改（见 AGENTS.md 已知缺陷登记）。
        /// </remarks>
        public bool IsCompleted(ProgressSnapshot snapshot)
        {
            if (snapshot?.Payload is not DataSheetProgressDto dto) return false;

            return dto.Total > 0
                && (dto.Status == "SUCCESS"
                    || dto.Status == "FAILED"
                    || dto.Status == "PARTIAL_FAILED");
        }

        /// <inheritdoc />
        /// <remarks>
        /// ★ 必须逐字复刻原 <c>DataSheetProgressHub.ComputeHash</c> 的实现：
        /// 字段顺序、分隔符（<c>|</c>、<c>,</c>、<c>:</c>）、<c>Ticks</c> 精度任一改变，
        /// 都会让迁移后首次推送算出一个"新"哈希而多推一次（功能无损但属行为变更）。
        /// </remarks>
        public string ComputeHash(ProgressSnapshot snapshot)
        {
            if (snapshot?.Payload is not DataSheetProgressDto dto) return string.Empty;

            var raw = $"{dto.Status}|{dto.Success}|{dto.Failed}|{dto.Generating}|{dto.Pending}|{dto.MergedPdfUrl}|" +
                      string.Join(",", dto.Items.Select(i => $"{i.DataSheetId}:{i.Status}:{i.UpdatedAt.Ticks}"));

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

            return Convert.ToHexString(bytes);
        }
    }
}
