using Microsoft.AspNetCore.Mvc;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.DataSheetConetxt;
using NX_lims_Softlines_Command_System.src.Application.Service.DataSheetContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Infrastructure.Realtime.Sse;

namespace NX_lims_Softlines_Command_System.src.Web_API
{
    [ApiController]
    [Route("api/[Controller]")]
    public class DataSheetController : ControllerBase
    {
        private readonly DataSheetService _dataSheetService;
        private readonly DataSheetProgressService _progressService;
        private readonly DataSheetProgressHub _hub;
        private readonly ILogger<HttpResponseSseClient> _sseLogger;

        public DataSheetController(
            DataSheetService dataSheetService,
            DataSheetProgressService progressService,
            DataSheetProgressHub hub,
            ILogger<HttpResponseSseClient> sseLogger)
        {
            _dataSheetService = dataSheetService;
            _progressService = progressService;
            _hub = hub;
            _sseLogger = sseLogger;
        }

        /// <summary>
        /// Start Generate DataSheet
        /// </summary>
        /// <param name="checkListId"></param>
        /// <param name="dto"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("datasheet-generate")]
        public async Task<Result<GenerateDataSheetTaskResult>> Generate([FromBody] DataSheetGenerateDto dto,CancellationToken ct)
        {
            var result = await _dataSheetService.GenerateTaskStart(dto, ct);

            return result;
        }

        [HttpPost("retry/{dataSheetId}")]
        public async Task<Result> RetryDatasheet(Guid dataSheetId, CancellationToken ct)
        {
            return await _dataSheetService.RetryAsync(dataSheetId, ct);
        }

        [HttpPost("retry-batch/{checkListId}")]
        public async Task<Result> RetryBatch(Guid checkListId, CancellationToken ct)
        {
            return await _dataSheetService.RetryBatchAsync(new CheckListId(checkListId), ct);
        }


        /// <summary>
        /// 工作单生成进度
        /// </summary>
        /// <param name="checkListId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("datasheet-progress")]
        public async Task<Result<DataSheetProgressDto>> GetProgress(Guid checkListId, CancellationToken ct)
        {
            var snapshot = await _progressService.GetSnapshot(
                new CheckListId(checkListId), ct);

            return Result<DataSheetProgressDto>.Ok(snapshot);
        }

        /// <summary>
        /// 工作单生成进度流
        /// </summary>
        /// <param name="checkListId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("datasheet-progress/stream")]
        public async Task StreamProgress(Guid checkListId, CancellationToken ct)
        {
            // header 配置与 SSE 客户端都已收敛到 Infrastructure\Realtime\Sse，
            // 第二条链路接入时复制这段骨架即可，不会再漏掉 X-Accel-Buffering。
            SseResponseConfigurator.Configure(Response);

            var client = new HttpResponseSseClient(Response, _sseLogger);
            await _hub.RegisterAsync(checkListId, client, ct);

            try
            {
                var snapshot = await _progressService.GetSnapshot(
                    new CheckListId(checkListId), ct);

                // 首帧立即推一次：前端刚连上就能拿到当前进度，不必等下一轮轮询。
                await client.SendAsync("progress", snapshot, ct);

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(15000, ct);
                    await client.SendAsync("heartbeat", new { ts = DateTime.UtcNow }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开属正常结束路径，不需要记错误日志
            }
            finally
            {
                await _hub.UnregisterAsync(checkListId, client);
            }
        }

    }
}
