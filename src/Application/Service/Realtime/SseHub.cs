using System.Collections.Concurrent;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse;

namespace NX_lims_Softlines_Command_System.src.Application.Service.Realtime
{
    /// <summary>
    /// <see cref="ISseHub"/> 的实现：进程内连接表 + 哈希去重 + dead 连接清理。
    ///
    /// 单例是必须的：连接集合要跨请求共享，否则每个请求看到的是各自的空集合。
    ///
    /// ⚠️ 多实例部署限制：连接表在进程内存中，多副本时副本 A 的订阅收不到副本 B 的推送。
    /// 本类**不做**跨实例广播，上线多副本前必须先完成本目录 AGENTS.md 里列出的改造清单。
    /// </summary>
    public class SseHub : ISseHub, ISingletonDependency
    {
        /// <summary>
        /// 连接表：<c>"channel:key"</c> → 该业务 key 下的所有连接。
        /// 用拼接字符串做复合键而不是元组，是为了让「按频道前缀筛选活跃 key」只做一次字符串操作。
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentBag<ISseClient>> _clients
            = new(StringComparer.Ordinal);

        /// <summary>最近一次已推送快照的哈希：<c>"channel:key"</c> → hash。</summary>
        private readonly ConcurrentDictionary<string, string> _lastSnapshotHash
            = new(StringComparer.Ordinal);

        private readonly ILogger<SseHub> _logger;

        /// <summary>
        /// 构造推送枢纽。
        /// </summary>
        /// <param name="logger">日志；用于记录 dead 连接与广播失败，避免静默吞异常。</param>
        /// <exception cref="ArgumentNullException">logger 为 null。</exception>
        public SseHub(ILogger<SseHub> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task RegisterAsync(string channel, string key, ISseClient client, CancellationToken ct = default)
        {
            ValidateChannelKey(channel, key);
            if (client == null) throw new ArgumentNullException(nameof(client));

            var compositeKey = BuildKey(channel, key);
            var bag = _clients.GetOrAdd(compositeKey, _ => new ConcurrentBag<ISseClient>());
            bag.Add(client);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UnregisterAsync(string channel, string key, ISseClient client)
        {
            ValidateChannelKey(channel, key);
            if (client == null) throw new ArgumentNullException(nameof(client));

            var compositeKey = BuildKey(channel, key);
            if (!_clients.TryGetValue(compositeKey, out var bag)) return Task.CompletedTask;

            // ConcurrentBag 不支持按项删除，只能过滤后重建再整体替换（沿用既有实现的做法）。
            var remaining = new ConcurrentBag<ISseClient>(bag.Where(c => !ReferenceEquals(c, client)));

            if (remaining.IsEmpty)
            {
                // ★ 与原实现的关键差异：最后一个连接移除后把 key 一起摘掉。
                //   原 DataSheetProgressHub 保留空 Bag，导致 GetActiveCheckListIds 仍返回该 key，
                //   轮询服务会继续为「已无人订阅」的 key 查库，与「无订阅零 DB 开销」的设计目标不符。
                _clients.TryRemove(compositeKey, out _);
            }
            else
            {
                _clients[compositeKey] = remaining;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> GetActiveKeys(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return Array.Empty<string>();

            var prefix = channel + ":";

            return _clients
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && !kv.Value.IsEmpty)
                .Select(kv => kv.Key.Substring(prefix.Length))
                .ToList();
        }

        /// <inheritdoc />
        public bool HasChanged(string channel, string key, string hash)
        {
            ValidateChannelKey(channel, key);

            var compositeKey = BuildKey(channel, key);
            var last = _lastSnapshotHash.GetOrAdd(compositeKey, _ => string.Empty);

            if (string.Equals(last, hash, StringComparison.Ordinal)) return false;

            _lastSnapshotHash[compositeKey] = hash;
            return true;
        }

        /// <inheritdoc />
        public async Task BroadcastAsync(string channel, string key, string eventName, object payload, CancellationToken ct = default)
        {
            ValidateChannelKey(channel, key);

            var compositeKey = BuildKey(channel, key);
            if (!_clients.TryGetValue(compositeKey, out var bag)) return;

            // 单个连接写入失败不能中断其它订阅者，因此先收集、后统一清理。
            List<ISseClient>? dead = null;

            foreach (var client in bag)
            {
                try
                {
                    await client.SendAsync(eventName, payload, ct);
                }
                catch (Exception ex)
                {
                    (dead ??= new List<ISseClient>()).Add(client);
                    _logger.LogDebug(ex,
                        "SSE 广播失败，连接 {ClientId}（channel={Channel}，key={Key}）将被清理。",
                        client.Id, channel, key);
                }
            }

            if (dead == null) return;

            foreach (var client in dead)
            {
                await UnregisterAsync(channel, key, client);
            }
        }

        /// <inheritdoc />
        public async Task CloseAllAsync(string channel, string key, CancellationToken ct = default)
        {
            ValidateChannelKey(channel, key);

            var compositeKey = BuildKey(channel, key);

            if (_clients.TryRemove(compositeKey, out var bag))
            {
                foreach (var client in bag)
                {
                    try
                    {
                        await client.CompleteAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        // 关闭失败不影响流程：连接本身可能已断，属正常路径。
                        _logger.LogDebug(ex, "SSE 连接 {ClientId} 关闭失败，已忽略。", client.Id);
                    }
                }
            }

            _lastSnapshotHash.TryRemove(compositeKey, out _);
        }

        /// <summary>复合键：频道与业务主键用冒号拼接。</summary>
        private static string BuildKey(string channel, string key) => $"{channel}:{key}";

        /// <summary>频道与 key 均为必填，空白值会导致连接表出现难以排查的键。</summary>
        private static void ValidateChannelKey(string channel, string key)
        {
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("频道名不能为空", nameof(channel));

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("业务 key 不能为空", nameof(key));
        }
    }
}
