using DocumentFormat.OpenXml.Office2010.Excel;
using Mapster;
using Microsoft.Extensions.Logging;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.TemplateConditionContext;
using NX_lims_Softlines_Command_System.src.Application.Contract.DTOs.CheckListContext;
using NX_lims_Softlines_Command_System.src.Application.Interface;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.CheckListContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext.Enums;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.ParamEngineContext.ConditionPoolContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository.ParamEngineContext;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Service.Engine;
using NX_lims_Softlines_Command_System.src.Domain.Share;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Domain.Share.Interface;
using NX_lims_Softlines_Command_System.src.Infrastructure.Service;
using NX_lims_Softlines_Command_System.src.Infrastructure.TemplateEngine.WordTemplateAdapter;
using System.Reflection.Emit;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Text.Json;

namespace NX_lims_Softlines_Command_System.src.Application.Service.CheckListContext
{
    public class CheckListAppService:IScopedDependency,ICheckListAppService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICheckListRepository _checkListRepository;
        private readonly IFileStorageService _fileStorage;
        private readonly IConditionPoolRepository  _conditionPoolRepository;
        private readonly IParamGenerationUseCaseService _paramGenerationUseCaseService;
        private readonly CheckListAdapter _checkListAdapter;

        /// <summary>checklist 侧 testParam 补齐服务（后端自算 testcondition 文本）。</summary>
        private readonly ICheckListTestParamResolver _testParamResolver;

        /// <summary>日志。用于记录带结构化字段的合成失败原因，便后后续审计模块接入。</summary>
        private readonly ILogger<CheckListAppService> _logger;

        public CheckListAppService(
            IUnitOfWork unitOfWork, 
            ICheckListRepository checkListRepository,
            CheckListAdapter checkListAdapter,
            IFileStorageService fileStorage,
            IConditionPoolRepository conditionPoolRepository,
            IParamGenerationUseCaseService paramGenerationUseCaseService,
            ICheckListTestParamResolver testParamResolver,
            ILogger<CheckListAppService> logger)
        {
            _unitOfWork = unitOfWork;
            _fileStorage = fileStorage;
            _checkListAdapter = checkListAdapter;
            _checkListRepository = checkListRepository;
            _conditionPoolRepository = conditionPoolRepository;
            _paramGenerationUseCaseService = paramGenerationUseCaseService;
            _testParamResolver = testParamResolver;
            _logger = logger;
        }

        /// <summary>
        /// 添加测试清单
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<Guid>> AddCheckList(AddCheckListDto dto,CancellationToken ct) 
        {
            try {
                var checkList = dto.Adapt<CheckList>();//已在Mapping调用工厂方法统一创建

                await _checkListRepository.AddAsync(checkList, ct);

                await _unitOfWork.SaveChangesAsync(ct);

                return Result<Guid>.Ok(checkList.Id.Value);
            }
            catch(Exception ex) 
            {
                return Result<Guid>.Fail(ex.InnerException?.Message ?? ex.Message);
            }
        }

        /// <summary>
        /// 更新测试清单
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<Guid>> UpdateCheckList(UpdateCheckListDto dto, CancellationToken ct)
        {
            try 
            {
                var checkList = await _checkListRepository.GetByIdAsync(new CheckListId(dto.Id), ct);

                if (checkList == null)
                    return Result<Guid>.Fail($"未找到Id为 {dto.Id} 的测试清单");

                // 3. DTO 转换为领域对象/值对象 (使用 Mapster 或手动映射)
                // 注意：这里假设 UpdateCheckListDto 里有 Items 和 Remark 属性
                var newItems = dto.Items?.Adapt<IReadOnlyList<CheckListItem>>();
                var newRemark = dto.Remark;

                // 4. 调用领域对象的 Update 方法，由领域对象内部保证业务规则和一致性
                checkList.Update(newItems, newRemark);

                await _checkListRepository.UpdateAsync(checkList, ct);

                await _unitOfWork.SaveChangesAsync(ct);
                 
                return Result<Guid>.Ok(checkList.Id.Value);
            }
            catch (Exception ex)
            {
                return Result<Guid>.Fail(ex.InnerException?.Message ?? ex.Message);
            }
        }

        /// <summary>
        /// 获取测试清单
        /// </summary>
        /// <param name="checkListId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<CheckListResponseDto>> GetCheckListAsync(Guid checkListId, CancellationToken ct)
        {
            var checkList = await _checkListRepository.GetByIdAsync(new CheckListId(checkListId), ct);
            if (checkList == null)
                return Result<CheckListResponseDto>.Fail("未能找到测试清单");

            var checkListDto = checkList.Adapt<CheckListResponseDto>();

            var items = new List<CheckListResponseItemDto>();

            foreach (var item in checkListDto.Items)
            {
                var newItem = item with { };  // 复制
                if (!string.IsNullOrWhiteSpace(newItem.Parameter))
                {
                    // 一次性处理提取和清理
                    var (cuttingMethod, cleanedParameter) = ProcessParameterCutting(newItem.Parameter);

                    if (!string.IsNullOrEmpty(cuttingMethod))
                    {
                        newItem.CuttingMethod = cuttingMethod;
                    }

                    // 无论是否提取到了 cutting，都将清理过（移除了指定字段）的 Parameter 赋回去
                    newItem.Parameter = cleanedParameter;
                }
                items.Add(newItem);
            }
            checkListDto.Items = items;

            return Result<CheckListResponseDto>.Ok(checkListDto);
        }

        /// <summary>
        /// 生成纸质版测试清单
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result<DocxUrlResponseDto>> GenerateCheckListAsync(CheckListGenerateDto dto, CancellationToken ct) 
        {
            var checklist = await _checkListRepository.GetByIdAsync(new CheckListId(dto.CheckListId), ct);

            //根据checklistId生成barcode
            using var barcodeBitmap = BarcodeGenerator.GenerateBarcode(checklist.Id, 100, 35);

            //生成copy文件
            string docxFileName = $"{checklist.OderId}_{DateTime.Now:yyMMddHHmmss}_CheckList.docx";

            string targetDocxPath = _fileStorage.CopyTemplate(
                Path.Combine("DocxModel","Checklist.docx"),
                Path.Combine("DocxModel", "SaveDocx"),
                docxFileName);

            try 
            {
                // 后端自算 testParam：覆盖 dto 条目的 Parameter（testcondition 文本）与去重后的 CuttingMethod。
                // 这是唯一的业务注入点 —— CheckListAdapter 保持纯 OpenXML 渲染，不注入任何业务依赖。
                try
                {
                    await _testParamResolver.ApplyAsync(checklist, dto, ct);
                }
                catch (TestConditionCompositionException ex)
                {
                    // 用户诉求：后端报错必须让操作人员知道原因。
                    // 结构化字段先入日志；后续审计模块可在此替换为写「报错原因表」。
                    _logger.LogError(ex,
                        "checklist testParam 合成失败，已阻断生成。Code={Code}, CheckListId={CheckListId}, TestPoint={TestPoint}, Detail={Detail}",
                        ex.Code, ex.CheckListId, ex.TestPoint, ex.Detail);
                    throw;
                }

                //调用ChecklistAdapter
                await _checkListAdapter.FillCheckListAsync(targetDocxPath, dto, barcodeBitmap);

                //生成成功后改变checklist状态为InProgress，保存至数据库
                checklist.ChangeInProcess();

                await _checkListRepository.UpdateAsync(checklist, ct);

                //返回生成的文件的Url，后续可能要把checklist的url保存进入数据库留档

                await _unitOfWork.SaveChangesAsync(ct);

                return Result<DocxUrlResponseDto>.Ok(new DocxUrlResponseDto
                {
                    fileKey = docxFileName,
                    fileName = docxFileName,
                    downloadUrl = $"/api/Review/checklist-{docxFileName}/download"
                });
            }
            catch (Exception ex) 
            {
                return Result<DocxUrlResponseDto>.Fail(ex.InnerException?.Message ?? ex.Message);
            }
        }

        /// <summary>
        /// 计算参数
        /// </summary>
        /// <param name="checkListId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<Result> CalculateParamAsync(Guid id, CancellationToken ct) 
        {
            var checkListId = new CheckListId(id);

            // 1. 获取聚合根
            var checkList = await _checkListRepository.GetByIdAsync(checkListId, ct);
            if (checkList == null)
                return Result.Fail("未能找到该测试清单");

            // 2. 获取条件池
            var existingPools = await _conditionPoolRepository.GetByCheckListIdAsync(checkListId, ct);

            // 3. 领域计算：在应用层组装计算结果字典
            var calculatedItemParams = new Dictionary<Guid, IReadOnlyDictionary<string, ParamSet?>>();

            foreach (var item in checkList.Items) // 通过聚合根暴露的只读集合遍历
            {
                var testPointParams = new Dictionary<string, ParamSet?>();

                foreach (var testPoint in item.Samples)
                {
                    var pool = existingPools.FirstOrDefault(p => p.TestPoints.Contains(testPoint));
                    var result = await _paramGenerationUseCaseService.GenerateForCheckListItemAsync(item, pool, ct);

                    if (!result.IsSuccess)
                        return Result.Fail($"生成测试项 {item.Id} 的测点 {testPoint} 参数时发生错误: {result.Error}");

                    testPointParams.Add(testPoint, result.Value);
                }

                // 将单个 Item 的计算结果加入总字典
                calculatedItemParams.Add(item.Id, testPointParams);
            }

            // 4. 调用聚合根的领域行为，一次性更新所有参数
            checkList.UpdateItemParameters(calculatedItemParams);

            // 5. 持久化
            await _checkListRepository.UpdateAsync(checkList, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Ok();
        }


        /// <summary>
        /// 提取 CuttingMethod 并从 Parameter 中移除 SpecimenArea 和 SpecimenNum
        /// </summary>
        /// <param name="parameter">原始 Parameter JSON 字符串</param>
        /// <returns>Item1: CuttingMethod字符串 (可能为null), Item2: 清理后的 Parameter JSON 字符串</returns>
        private static (string? CuttingMethod, string? CleanedParameter) ProcessParameterCutting(string parameter)
        {
            if (string.IsNullOrWhiteSpace(parameter))
                return (null, parameter);

            try
            {
                var root = JsonSerializer.Deserialize<JsonElement>(parameter);
                var parts = new List<string>();

                // 创建一个新的 Dictionary 来重建清理后的 JSON 结构
                var newRootDict = new Dictionary<string, JsonElement>();

                foreach (var tp in root.EnumerateObject())
                {
                    if (tp.Value.ValueKind != JsonValueKind.Object)
                    {
                        // 非 Object 节点直接保留
                        newRootDict[tp.Name] = tp.Value;
                        continue;
                    }

                    // 获取 values 对象
                    if (!tp.Value.TryGetProperty("values", out JsonElement values)
                        || values.ValueKind != JsonValueKind.Object)
                    {
                        // 没有 values 节点或 values 非 Object，直接保留原样
                        newRootDict[tp.Name] = tp.Value;
                        continue;
                    }

                    // --- 开始提取逻辑 ---
                    string? specimenArea = null;
                    if (values.TryGetProperty("SpecimenArea", out JsonElement area))
                    {
                        specimenArea = area.ValueKind == JsonValueKind.String
                            ? area.GetString()
                            : area.ToString();
                    }

                    string? specimenNum = null;
                    if (values.TryGetProperty("SpecimenNum", out JsonElement num))
                    {
                        specimenNum = num.ValueKind == JsonValueKind.String
                            ? num.GetString()
                            : num.ToString();
                    }

                    if (!string.IsNullOrEmpty(specimenArea) || !string.IsNullOrEmpty(specimenNum))
                    {
                        var groupParts = new List<string>();
                        if (!string.IsNullOrEmpty(specimenArea))
                            groupParts.Add($"SpecimenArea={specimenArea}");
                        if (!string.IsNullOrEmpty(specimenNum))
                            groupParts.Add($"SpecimenNum={specimenNum}");

                        parts.Add(string.Join(", ", groupParts));
                    }

                    // --- 开始移除逻辑 (重建 values 对象) ---
                    var newValuesDict = new Dictionary<string, JsonElement>();
                    foreach (var valProp in values.EnumerateObject())
                    {
                        // 过滤掉 SpecimenArea 和 SpecimenNum
                        if (valProp.NameEquals("SpecimenArea") || valProp.NameEquals("SpecimenNum"))
                        {
                            continue;
                        }
                        newValuesDict[valProp.Name] = valProp.Value;
                    }

                    // --- 重建 tp 对象 ---
                    var newTpDict = new Dictionary<string, JsonElement>();
                    foreach (var tpProp in tp.Value.EnumerateObject())
                    {
                        if (tpProp.NameEquals("values"))
                        {
                            // 将重建的 values 字典序列化为 JsonElement 放入
                            using var doc = JsonSerializer.SerializeToDocument(newValuesDict);
                            newTpDict["values"] = doc.RootElement.Clone();
                        }
                        else
                        {
                            newTpDict[tpProp.Name] = tpProp.Value;
                        }
                    }

                    // 将重建的 tp 放入新的根字典
                    using var tpDoc = JsonSerializer.SerializeToDocument(newTpDict);
                    newRootDict[tp.Name] = tpDoc.RootElement.Clone();
                }

                // 重建最终的 JSON 字符串
                var cleanedParameter = JsonSerializer.Serialize(newRootDict);
                var cuttingMethod = parts.Count > 0 ? string.Join("; ", parts) : null;

                return (cuttingMethod, cleanedParameter);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to parse parameter: {ex.Message}");
                return (null, parameter); // 发生异常时，返回原始 parameter，避免数据丢失
            }
        }

    }
}
