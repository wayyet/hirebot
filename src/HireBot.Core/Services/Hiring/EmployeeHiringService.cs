using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    IDiscoveryRuleProvider discoveryRuleProvider,
    HiringStageCompletionEvaluator stageCompletionEvaluator,
    IHiringRuntimeStore hiringRuntimeStore,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<EmployeeHiringService> logger) : IEmployeeHiringService
{
    private const string KingCrewClientName = "KingCrew";
    private const string DefaultHireBotApiPrefix = "/api/integration/hirebot";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, HireOwnerContext> hireOwners = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ApiResponse<HireTemplateResultDto>> HireAsync(
        string templateId,
        HireTemplateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, "templateId 不能为空");
        }

        request ??= new HireTemplateRequestDto();
        var (tenantId, operatorId) = ResolveTenantAndOperator(request.TenantId, request.OperatorId);

        var normalizedTemplateId = templateId.Trim();
        var template = await templateDataProvider.GetByIdAsync(normalizedTemplateId, cancellationToken);
        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        TemplatePackageDefinition templatePackage;
        DiscoverySkillDefinition discoverySkill;
        try
        {
            templatePackage = await templatePackageProvider.LoadAsync(normalizedTemplateId, cancellationToken);
            discoverySkill = await discoveryRuleProvider.LoadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load template/discovery assets. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(500, "模板资产或 discovery skill 读取失败");
        }

        var ownerSubject = ResolveOwnerSubject(tenantId, operatorId);
        var remoteRequest = new KingCrewHireRequest(
            TemplateId: normalizedTemplateId,
            TenantId: tenantId,
            OperatorId: operatorId,
            UseCase: request.UseCase);

        var call = await SendForJsonAsync<HireTemplateResultDto>(
            HttpMethod.Post,
            "/hirings",
            remoteRequest,
            ownerSubject,
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var systemSkillCall = await UploadDiscoverySystemSkillAsync(
            call.Data.HireId,
            discoverySkill,
            ownerSubject,
            cancellationToken);
        if (!systemSkillCall.Success || systemSkillCall.Data is null)
        {
            logger.LogWarning(
                "Discovery system skill upload failed. HireId={HireId}, TemplateId={TemplateId}, StatusCode={StatusCode}, Message={Message}",
                call.Data.HireId,
                normalizedTemplateId,
                systemSkillCall.StatusCode,
                systemSkillCall.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(systemSkillCall.StatusCode, systemSkillCall.Message);
        }

        var templatePackageCall = await UploadTemplatePackageAsync(
            call.Data.HireId,
            templatePackage,
            ownerSubject,
            cancellationToken);
        if (!templatePackageCall.Success || templatePackageCall.Data is null)
        {
            logger.LogWarning(
                "Template package upload failed. HireId={HireId}, TemplateId={TemplateId}, StatusCode={StatusCode}, Message={Message}",
                call.Data.HireId,
                normalizedTemplateId,
                templatePackageCall.StatusCode,
                templatePackageCall.Message);
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(templatePackageCall.StatusCode, templatePackageCall.Message);
        }

        var initialStageCompletion = stageCompletionEvaluator.Evaluate(
            discoverySkill.StageRules,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        hireOwners[call.Data.HireId] = new HireOwnerContext(
            OwnerSubject: ownerSubject,
            TenantId: tenantId,
            OperatorId: operatorId,
            TemplateId: normalizedTemplateId,
            TemplateName: template.Name,
            EmployeeId: null);
        hiringRuntimeStore.Upsert(new HiringRuntimeContext
        {
            HireId = call.Data.HireId,
            TemplateId = normalizedTemplateId,
            TemplateName = template.Name,
            OwnerSubject = ownerSubject,
            TenantId = tenantId,
            OperatorId = operatorId,
            SandboxId = call.Data.SandboxId,
            CurrentStage = HiringCollectionStage.Goal,
            CollectionPhase = HiringCollectionPhase.NotStarted,
            TemplatePackage = templatePackage,
            DiscoverySkill = discoverySkill,
            StructuredData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            Materials = [],
            StageCompletion = initialStageCompletion
        });

        logger.LogInformation(
            "Template hire submitted to KingCrew with discovery system skill and template package uploaded. HireId={HireId}, TemplateId={TemplateId}, SkillId={SkillId}, SkillVersion={SkillVersion}, PackageId={PackageId}, PackageVersion={PackageVersion}, Owner={Owner}",
            call.Data.HireId,
            normalizedTemplateId,
            systemSkillCall.Data.SkillId,
            systemSkillCall.Data.SkillVersion,
            templatePackageCall.Data.PackageId,
            templatePackageCall.Data.PackageVersion,
            ownerSubject);

        return ApiResponse<HireTemplateResultDto>.SuccessResponse(call.Data, "雇佣任务已创建");
    }

    public async Task<ApiResponse<HiringStatusDto>> GetHiringStatusAsync(string hireId, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringStatusDto>.ErrorResponse(400, error);
        }

        var call = await SendForJsonAsync<HiringStatusDto>(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringStatusDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        return ApiResponse<HiringStatusDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<StartHiringConversationResultDto>> StartConversationAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(400, error);
        }

        var call = await SendForJsonAsync<StartHiringConversationResultDto>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/conversation/start",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            var currentStage = ResolveCurrentStage(runtimeContext.StageCompletion, call.Data.CurrentStage);
            var collectionPhase = ResolveCollectionPhase(
                runtimeContext.StageCompletion,
                runtimeContext.StructuredData,
                runtimeContext.CollectionPhase);
            runtimeContext = runtimeContext with
            {
                SessionId = call.Data.SessionId,
                CurrentStage = currentStage,
                CollectionPhase = collectionPhase
            };
            hiringRuntimeStore.Upsert(runtimeContext);
            call = RemoteCallResult<StartHiringConversationResultDto>.Ok(call.Data with
            {
                CurrentStage = currentStage,
                StageSkills = BuildStageSkills(runtimeContext.DiscoverySkill)
            });
        }

        return ApiResponse<StartHiringConversationResultDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<HiringConversationResultDto>> SendConversationMessageAsync(
        string hireId,
        HiringConversationMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var idError))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, idError);
        }

        if (request is null ||
            (string.IsNullOrWhiteSpace(request.Content) &&
             (request.StructuredAnswers is null || request.StructuredAnswers.Count == 0) &&
             (request.Materials is null || request.Materials.Count == 0)))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, "content 与 structuredAnswers 不能同时为空");
        }

        var call = await SendForJsonAsync<HiringConversationResultDto>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/conversation/messages",
            request,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            var structuredData = NormalizeStructuredData(call.Data.LatestPreview.StructuredData);
            var materials = MergeMaterials(runtimeContext.Materials, BuildMaterialsFromRequest(request));
            var stageCompletion = stageCompletionEvaluator.Evaluate(runtimeContext.DiscoverySkill.StageRules, structuredData);
            var currentStage = ResolveCurrentStage(stageCompletion, call.Data.CurrentStage);
            var collectionPhase = ResolveCollectionPhase(stageCompletion, structuredData, HiringCollectionPhase.InProgress);
            var preview = EnrichStagePreview(
                call.Data.LatestPreview,
                runtimeContext.DiscoverySkill,
                stageCompletion,
                currentStage,
                collectionPhase,
                structuredData);

            runtimeContext = runtimeContext with
            {
                SessionId = call.Data.SessionId,
                CurrentStage = currentStage,
                CollectionPhase = collectionPhase,
                StructuredData = structuredData,
                Materials = materials,
                StageCompletion = stageCompletion
            };
            hiringRuntimeStore.Upsert(runtimeContext);

            call = RemoteCallResult<HiringConversationResultDto>.Ok(call.Data with
            {
                CurrentStage = currentStage,
                LatestPreview = preview
            });
        }

        return ApiResponse<HiringConversationResultDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<HiringConversationTimelineDto>> GetConversationTimelineAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(400, error);
        }

        var call = await SendForJsonAsync<HiringConversationTimelineDto>(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/conversation/messages",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is not null)
        {
            call = RemoteCallResult<HiringConversationTimelineDto>.Ok(call.Data with
            {
                CurrentStage = runtimeContext.CurrentStage,
                CollectionPhase = runtimeContext.CollectionPhase,
                StageSkills = BuildStageSkills(runtimeContext.DiscoverySkill)
            });
        }

        return ApiResponse<HiringConversationTimelineDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<HiringStagePreviewDto>> GetStagePreviewAsync(
        string hireId,
        string? stage,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringStagePreviewDto>.ErrorResponse(400, error);
        }

        var suffix = string.IsNullOrWhiteSpace(stage)
            ? string.Empty
            : $"?stage={Uri.EscapeDataString(stage.Trim())}";
        var call = await SendForJsonAsync<HiringStagePreviewDto>(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/stage-preview{suffix}",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringStagePreviewDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            var structuredData = NormalizeStructuredData(call.Data.StructuredData);
            var stageCompletion = stageCompletionEvaluator.Evaluate(runtimeContext.DiscoverySkill.StageRules, structuredData);
            var currentStage = ResolveCurrentStage(stageCompletion, call.Data.Stage);
            var collectionPhase = ResolveCollectionPhase(stageCompletion, structuredData, runtimeContext.CollectionPhase);
            var preview = EnrichStagePreview(
                call.Data,
                runtimeContext.DiscoverySkill,
                stageCompletion,
                currentStage,
                collectionPhase,
                structuredData);

            runtimeContext = runtimeContext with
            {
                CurrentStage = currentStage,
                CollectionPhase = collectionPhase,
                StructuredData = structuredData,
                StageCompletion = stageCompletion
            };
            hiringRuntimeStore.Upsert(runtimeContext);

            call = RemoteCallResult<HiringStagePreviewDto>.Ok(preview);
        }

        return ApiResponse<HiringStagePreviewDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<HiringAuditDecisionResultDto>> SubmitAuditDecisionAsync(
        string hireId,
        HiringAuditDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var idError))
        {
            return ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, idError);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Stage) || string.IsNullOrWhiteSpace(request.Decision))
        {
            return ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(400, "stage 与 decision 为必填项");
        }

        var call = await SendForJsonAsync<HiringAuditDecisionResultDto>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/audit-decisions",
            request,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringAuditDecisionResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is not null)
        {
            runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken) ?? runtimeContext;
            call = RemoteCallResult<HiringAuditDecisionResultDto>.Ok(call.Data with
            {
                CurrentStage = runtimeContext.CurrentStage,
                CollectionPhase = runtimeContext.CollectionPhase
            });
        }

        return ApiResponse<HiringAuditDecisionResultDto>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<IReadOnlyList<HiringAuditLogDto>>> GetAuditLogsAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<IReadOnlyList<HiringAuditLogDto>>.ErrorResponse(400, error);
        }

        var call = await SendForJsonAsync<List<HiringAuditLogDto>>(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/audit-logs",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<IReadOnlyList<HiringAuditLogDto>>.ErrorResponse(call.StatusCode, call.Message);
        }

        return ApiResponse<IReadOnlyList<HiringAuditLogDto>>.SuccessResponse(call.Data);
    }

    public async Task<ApiResponse<HiringFinalizeResultDto>> FinalizeAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(409, "本地雇佣上下文不存在，请重新发起雇佣流程");
        }

        var incompleteStages = runtimeContext.StageCompletion
            .Where(item => !item.ReadyForNextStage)
            .ToArray();
        if (incompleteStages.Length > 0)
        {
            var blockingFields = incompleteStages
                .SelectMany(item => item.BlockingFields.Select(field => $"{item.Stage}:{field}"))
                .ToArray();
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(
                409,
                $"仍有 discovery 阻塞字段未补齐：{string.Join("、", blockingFields)}");
        }

        var call = await SendForJsonAsync<HiringFinalizeResultDto>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/finalize",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var finalizeResult = call.Data;
        if (hireOwners.TryGetValue(normalizedHireId, out var ownerContext))
        {
            if (string.IsNullOrWhiteSpace(ownerContext.EmployeeId))
            {
                var capabilities = (await templateDataProvider.GetByIdAsync(ownerContext.TemplateId, cancellationToken))?.CoreAbilities ?? [];
                using var scope = serviceScopeFactory.CreateScope();
                var employeeRuntimeService = scope.ServiceProvider.GetRequiredService<IEmployeeRuntimeService>();
                var createResponse = await employeeRuntimeService.CreateFromHireAsync(
                    new CreateEmployeeFromHireRequestDto(
                        HireId: normalizedHireId,
                        TemplateId: ownerContext.TemplateId,
                        TemplateName: ownerContext.TemplateName,
                        OwnerSubject: ownerContext.OwnerSubject,
                        TenantId: ownerContext.TenantId,
                        OperatorId: ownerContext.OperatorId,
                        Capabilities: capabilities),
                    cancellationToken);

                if (createResponse.Success && createResponse.Data is not null)
                {
                    ownerContext = ownerContext with { EmployeeId = createResponse.Data.EmployeeId };
                    hireOwners[normalizedHireId] = ownerContext;
                }
            }

            if (!string.IsNullOrWhiteSpace(ownerContext.EmployeeId))
            {
                finalizeResult = finalizeResult with { EmployeeId = ownerContext.EmployeeId };
            }
        }

        var artifactArchiveCall = await SendForBytesAsync(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/artifacts/download",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);
        if (!artifactArchiveCall.Success || artifactArchiveCall.Data is null || artifactArchiveCall.Data.Length == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(artifactArchiveCall.StatusCode, artifactArchiveCall.Message);
        }

        var extractedArtifacts = ExtractZipEntries(artifactArchiveCall.Data);
        if (extractedArtifacts.Count == 0)
        {
            return ApiResponse<HiringFinalizeResultDto>.ErrorResponse(502, "后端交付包为空或无法解析");
        }

        runtimeContext = runtimeContext with
        {
            CurrentStage = finalizeResult.CurrentStage,
            CollectionPhase = finalizeResult.CollectionPhase,
            EmployeeId = finalizeResult.EmployeeId,
            ArtifactFiles = extractedArtifacts,
            ArtifactArchive = artifactArchiveCall.Data,
            ArtifactArchiveFileName = artifactArchiveCall.FileName
        };
        hiringRuntimeStore.Upsert(runtimeContext);

        finalizeResult = finalizeResult with
        {
            GeneratedFiles = extractedArtifacts.Keys.ToArray(),
            DownloadUrl = $"/api/v1/hirings/{normalizedHireId}/artifacts/download"
        };

        return ApiResponse<HiringFinalizeResultDto>.SuccessResponse(finalizeResult, "交付物已生成");
    }

    public async Task<ApiResponse<HiringWorkflowStateDto>> GetWorkflowStateAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringWorkflowStateDto>.ErrorResponse(400, error);
        }

        var call = await SendForJsonAsync<HiringWorkflowStateDto>(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/workflow",
            body: null,
            ResolveOwnerByHireId(normalizedHireId),
            cancellationToken);

        if (!call.Success || call.Data is null)
        {
            return ApiResponse<HiringWorkflowStateDto>.ErrorResponse(call.StatusCode, call.Message);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken);
        if (runtimeContext is not null)
        {
            call = RemoteCallResult<HiringWorkflowStateDto>.Ok(call.Data with
            {
                CurrentStage = runtimeContext.CurrentStage,
                CollectionPhase = runtimeContext.CollectionPhase,
                StageSkills = BuildStageSkills(runtimeContext.DiscoverySkill),
                TemplatePackageId = runtimeContext.TemplatePackage.PackageId,
                TemplatePackageVersion = runtimeContext.TemplatePackage.PackageVersion,
                DiscoverySkillId = runtimeContext.DiscoverySkill.SkillId,
                DiscoverySkillVersion = runtimeContext.DiscoverySkill.SkillVersion,
                StageCompletion = runtimeContext.StageCompletion
            });
        }

        return ApiResponse<HiringWorkflowStateDto>.SuccessResponse(call.Data);
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(400, error));
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext?.ArtifactArchive is null ||
            runtimeContext.ArtifactArchive.Length == 0 ||
            string.IsNullOrWhiteSpace(runtimeContext.ArtifactArchiveFileName))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(409, "交付包尚未生成，请先执行 finalize"));
        }

        return Task.FromResult(HiringArtifactDownloadResult.Success(
            runtimeContext.ArtifactArchiveFileName,
            "application/zip",
            runtimeContext.ArtifactArchive));
    }

    public Task<HiringArtifactDownloadResult> BuildArtifactFileDownloadAsync(
        string hireId,
        string artifactName,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(400, error));
        }

        if (string.IsNullOrWhiteSpace(artifactName))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(400, "artifactName cannot be empty"));
        }

        var normalizedArtifactName = Path.GetFileName(artifactName.Trim());
        if (!string.Equals(normalizedArtifactName, artifactName.Trim(), StringComparison.Ordinal))
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(400, "artifactName is invalid"));
        }

        var runtimeContext = hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext?.ArtifactFiles is null || runtimeContext.ArtifactFiles.Count == 0)
        {
            return Task.FromResult(HiringArtifactDownloadResult.Error(409, "交付物尚未生成，请先执行 finalize"));
        }

        if (!runtimeContext.ArtifactFiles.TryGetValue(normalizedArtifactName, out var content) || content.Length == 0)
        {
            return Task.FromResult(HiringArtifactDownloadResult.NotFound("交付物不存在"));
        }

        return Task.FromResult(HiringArtifactDownloadResult.Success(
            normalizedArtifactName,
            "application/json",
            content));
    }

    private async Task<HiringRuntimeContext?> RefreshRuntimeProgressAsync(string hireId, CancellationToken cancellationToken)
    {
        var runtimeContext = hiringRuntimeStore.Get(hireId);
        if (runtimeContext is null)
        {
            return null;
        }

        var previewCall = await SendForJsonAsync<HiringStagePreviewDto>(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(hireId)}/stage-preview",
            body: null,
            ResolveOwnerByHireId(hireId),
            cancellationToken);

        if (!previewCall.Success || previewCall.Data is null)
        {
            return runtimeContext;
        }

        var structuredData = NormalizeStructuredData(previewCall.Data.StructuredData);
        var stageCompletion = stageCompletionEvaluator.Evaluate(runtimeContext.DiscoverySkill.StageRules, structuredData);
        var currentStage = ResolveCurrentStage(stageCompletion, previewCall.Data.Stage);
        var collectionPhase = ResolveCollectionPhase(stageCompletion, structuredData, runtimeContext.CollectionPhase);

        runtimeContext = runtimeContext with
        {
            CurrentStage = currentStage,
            CollectionPhase = collectionPhase,
            StructuredData = structuredData,
            StageCompletion = stageCompletion
        };
        hiringRuntimeStore.Upsert(runtimeContext);

        return runtimeContext;
    }

    private static IReadOnlyList<StageSkillMappingDto> BuildStageSkills(DiscoverySkillDefinition discoverySkill)
    {
        return discoverySkill.StageRules
            .Select(rule => new StageSkillMappingDto(
                Stage: rule.Stage,
                SkillName: rule.SkillName,
                RequiredFields: rule.RequiredFields,
                Description: rule.Description))
            .ToArray();
    }

    private static HiringStagePreviewDto EnrichStagePreview(
        HiringStagePreviewDto preview,
        DiscoverySkillDefinition discoverySkill,
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string currentStage,
        string collectionPhase,
        IReadOnlyDictionary<string, string?> structuredData)
    {
        var currentRule = discoverySkill.StageRules.FirstOrDefault(rule =>
            string.Equals(rule.Stage, currentStage, StringComparison.OrdinalIgnoreCase));
        var currentCompletion = stageCompletion.FirstOrDefault(item =>
            string.Equals(item.Stage, currentStage, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<string> riskNotes;
        if (string.Equals(collectionPhase, HiringCollectionPhase.ReadyForFinalize, StringComparison.OrdinalIgnoreCase))
        {
            riskNotes = ["所有 discovery 阶段已满足，可执行 finalize 生成实例交付物。"];
        }
        else if (currentCompletion is not null && currentCompletion.BlockingFields.Count > 0)
        {
            riskNotes = [$"当前阶段仍缺少字段：{string.Join("、", currentCompletion.BlockingFields)}"];
        }
        else
        {
            riskNotes = ["当前阶段字段已齐全，可进入下一阶段。"];
        }

        return preview with
        {
            Stage = currentStage,
            SkillName = currentRule?.SkillName ?? preview.SkillName,
            StructuredData = structuredData,
            MissingFields = currentCompletion?.BlockingFields ?? preview.MissingFields,
            RiskNotes = riskNotes,
            ReadyForAudit = currentCompletion?.ReadyForNextStage ?? preview.ReadyForAudit
        };
    }

    private Task<RemoteCallResult<SystemSkillUploadResult>> UploadDiscoverySystemSkillAsync(
        string hireId,
        DiscoverySkillDefinition discoverySkill,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        return SendForJsonAsync<SystemSkillUploadResult>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(hireId)}/system-skills/upload",
            BuildSystemSkillUploadPayload(discoverySkill),
            ownerSubject,
            cancellationToken);
    }

    private Task<RemoteCallResult<TemplatePackageUploadResult>> UploadTemplatePackageAsync(
        string hireId,
        TemplatePackageDefinition templatePackage,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        return SendForJsonAsync<TemplatePackageUploadResult>(
            HttpMethod.Post,
            $"/hirings/{Uri.EscapeDataString(hireId)}/template-package/upload",
            BuildTemplatePackageUploadPayload(templatePackage),
            ownerSubject,
            cancellationToken);
    }

    private static SystemSkillUploadPayload BuildSystemSkillUploadPayload(DiscoverySkillDefinition discoverySkill)
    {
        return new SystemSkillUploadPayload(
            SkillId: discoverySkill.SkillId,
            SkillVersion: discoverySkill.SkillVersion,
            SkillHash: discoverySkill.SkillHash,
            Files: discoverySkill.Files
                .Select(file => new SystemSkillFileUploadPayload(
                    RelativePath: file.RelativePath,
                    ContentHash: file.ContentHash,
                    Content: file.Content))
                .ToArray(),
            StageRules: discoverySkill.StageRules
                .Select(rule => new SystemSkillStageRuleUploadPayload(
                    Stage: rule.Stage,
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());
    }

    private static TemplatePackageUploadPayload BuildTemplatePackageUploadPayload(TemplatePackageDefinition templatePackage)
    {
        return new TemplatePackageUploadPayload(
            PackageId: templatePackage.PackageId,
            PackageVersion: templatePackage.PackageVersion,
            PackageHash: templatePackage.PackageHash,
            ManifestJson: templatePackage.ManifestJson,
            OntologySlices: templatePackage.OntologySlices
                .Select(slice => new TemplateOntologySliceUploadPayload(
                    Name: slice.Name,
                    RelativePath: slice.RelativePath,
                    Type: slice.Type,
                    Required: slice.Required,
                    ContentHash: slice.ContentHash,
                    Content: slice.Content))
                .ToArray(),
            RequiredSkills: templatePackage.RequiredSkills
                .Select(skill => new TemplateSkillUploadPayload(
                    Name: skill.Name,
                    RelativePath: skill.RelativePath,
                    Required: skill.Required,
                    ContentHash: skill.ContentHash,
                    Content: skill.Content))
                .ToArray());
    }

    private static IReadOnlyList<HiringConversationMaterialDto> BuildMaterialsFromRequest(HiringConversationMessageRequestDto request)
    {
        var result = new List<HiringConversationMaterialDto>();
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var content = request.Content.Trim();
            result.Add(new HiringConversationMaterialDto
            {
                Type = "text",
                Name = $"conversation-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
                Content = content,
                ContentHash = ComputeContentHash(content),
                Size = Encoding.UTF8.GetByteCount(content),
                MimeType = "text/plain",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = "conversation"
                }
            });
        }

        if (request.Materials is not null)
        {
            foreach (var material in request.Materials)
            {
                var normalized = NormalizeMaterial(material);
                if (normalized is not null)
                {
                    result.Add(normalized);
                }
            }
        }

        return result;
    }

    private static HiringConversationMaterialDto? NormalizeMaterial(HiringConversationMaterialDto? material)
    {
        if (material is null)
        {
            return null;
        }

        var type = string.IsNullOrWhiteSpace(material.Type) ? "file" : material.Type.Trim();
        var name = string.IsNullOrWhiteSpace(material.Name)
            ? $"{type}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"
            : material.Name.Trim();
        var content = string.IsNullOrWhiteSpace(material.Content) ? null : material.Content;
        return material with
        {
            Type = type,
            Name = name,
            Content = content,
            ContentHash = string.IsNullOrWhiteSpace(material.ContentHash) && content is not null
                ? ComputeContentHash(content)
                : material.ContentHash,
            Size = material.Size ?? (content is null ? null : Encoding.UTF8.GetByteCount(content)),
            MimeType = string.IsNullOrWhiteSpace(material.MimeType) ? null : material.MimeType.Trim(),
            Metadata = material.Metadata?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<HiringConversationMaterialDto> MergeMaterials(
        IReadOnlyList<HiringConversationMaterialDto> existing,
        IReadOnlyList<HiringConversationMaterialDto> incoming)
    {
        if (incoming.Count == 0)
        {
            return existing;
        }

        var result = existing.ToList();
        foreach (var material in incoming)
        {
            var hasDuplicate = !string.IsNullOrWhiteSpace(material.ContentHash) &&
                               result.Any(existingMaterial => string.Equals(
                                   existingMaterial.ContentHash,
                                   material.ContentHash,
                                   StringComparison.OrdinalIgnoreCase));
            if (!hasDuplicate)
            {
                result.Add(material);
            }
        }

        return result;
    }

    private static string ComputeContentHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static Dictionary<string, string?> NormalizeStructuredData(IReadOnlyDictionary<string, string?>? source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            result[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return result;
    }

    private static string ResolveCurrentStage(
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string fallbackStage)
    {
        var nextStage = stageCompletion.FirstOrDefault(item => !item.ReadyForNextStage);
        if (nextStage is not null)
        {
            return nextStage.Stage;
        }

        return string.Equals(fallbackStage, HiringCollectionStage.Done, StringComparison.OrdinalIgnoreCase)
            ? HiringCollectionStage.Done
            : HiringCollectionStage.Package;
    }

    private static string ResolveCollectionPhase(
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        IReadOnlyDictionary<string, string?> structuredData,
        string fallbackPhase)
    {
        if (string.Equals(fallbackPhase, HiringCollectionPhase.Finalized, StringComparison.OrdinalIgnoreCase))
        {
            return HiringCollectionPhase.Finalized;
        }

        if (structuredData.Count == 0)
        {
            return HiringCollectionPhase.NotStarted;
        }

        return stageCompletion.All(item => item.ReadyForNextStage)
            ? HiringCollectionPhase.ReadyForFinalize
            : HiringCollectionPhase.InProgress;
    }

    private async Task<RemoteCallResult<T>> SendForJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(KingCrewClientName);
        if (client.BaseAddress is null)
        {
            return RemoteCallResult<T>.Failure(500, "KingCrew:BaseUrl 未配置");
        }

        using var request = CreateRequest(method, path, ownerSubject);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return RemoteCallResult<T>.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(content) ?? $"调用 KingCrew 接口失败（HTTP {(int)response.StatusCode}）");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return RemoteCallResult<T>.Failure(502, "调用 KingCrew 接口失败：响应为空");
            }

            var model = JsonSerializer.Deserialize<T>(content, JsonOptions);
            if (model is null)
            {
                return RemoteCallResult<T>.Failure(502, "调用 KingCrew 接口失败：响应解析为空");
            }

            return RemoteCallResult<T>.Ok(model);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 KingCrew 接口被取消. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(499, "请求已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 KingCrew 接口超时. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(504, "调用 KingCrew 接口超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 KingCrew 接口异常. Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(502, "调用 KingCrew 接口异常");
        }
    }

    private async Task<RemoteBinaryCallResult> SendForBytesAsync(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(KingCrewClientName);
        if (client.BaseAddress is null)
        {
            return RemoteBinaryCallResult.Failure(500, "KingCrew:BaseUrl 未配置");
        }

        using var request = CreateRequest(method, path, ownerSubject);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                return RemoteBinaryCallResult.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(payload) ?? $"调用 KingCrew 接口失败（HTTP {(int)response.StatusCode}）");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                return RemoteBinaryCallResult.Failure(502, "调用 KingCrew 接口失败：响应为空");
            }

            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                           response.Content.Headers.ContentDisposition?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                fileName = fileName.Trim().Trim('"');
            }

            return RemoteBinaryCallResult.Ok(
                fileName ?? "hirebot_artifacts.zip",
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                bytes);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "调用 KingCrew 二进制接口被取消. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(499, "请求已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "调用 KingCrew 二进制接口超时. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(504, "调用 KingCrew 接口超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 KingCrew 二进制接口异常. Method={Method}, Path={Path}", method, path);
            return RemoteBinaryCallResult.Failure(502, "调用 KingCrew 接口异常");
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ExtractZipEntries(byte[] archiveBytes)
    {
        using var memoryStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            result[entry.FullName.Replace('\\', '/')] = buffer.ToArray();
        }

        return result;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string ownerSubject)
    {
        var prefix = configuration["KingCrew:HireBotApiPrefix"];
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? DefaultHireBotApiPrefix
            : "/" + prefix.Trim().Trim('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        var request = new HttpRequestMessage(method, $"{normalizedPrefix}{normalizedPath}");

        var incomingAuthorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(incomingAuthorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", incomingAuthorization);
        }
        else
        {
            var staticToken = configuration["KingCrew:BearerToken"];
            if (!string.IsNullOrWhiteSpace(staticToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staticToken.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(ownerSubject))
        {
            request.Headers.TryAddWithoutValidation("X-HireBot-Owner", ownerSubject);
        }

        return request;
    }

    private static string? ExtractRemoteMessage(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                var message = messageElement.GetString();
                return string.IsNullOrWhiteSpace(message) ? null : message;
            }

            if (doc.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                var message = errorElement.GetString();
                return string.IsNullOrWhiteSpace(message) ? null : message;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveOwnerByHireId(string hireId)
    {
        if (hireOwners.TryGetValue(hireId, out var ownerContext))
        {
            return ownerContext.OwnerSubject;
        }

        var runtimeContext = hiringRuntimeStore.Get(hireId);
        if (runtimeContext is not null)
        {
            return runtimeContext.OwnerSubject;
        }

        return ResolveOwnerSubject();
    }

    private string ResolveOwnerSubject(string? tenantId = null, string? operatorId = null)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var sub =
            user?.FindFirst("sub")?.Value ??
            user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(sub))
        {
            return sub.Trim();
        }

        var ownerHeader = httpContextAccessor.HttpContext?.Request.Headers["X-HireBot-Owner"].ToString();
        if (!string.IsNullOrWhiteSpace(ownerHeader))
        {
            return ownerHeader.Trim();
        }

        var (resolvedTenantId, resolvedOperatorId) = ResolveTenantAndOperator(tenantId, operatorId);
        return $"{resolvedTenantId}:{resolvedOperatorId}";
    }

    private (string TenantId, string OperatorId) ResolveTenantAndOperator(string? tenantId, string? operatorId)
    {
        var user = httpContextAccessor.HttpContext?.User;

        var resolvedTenantId = FirstNonEmpty(
            tenantId,
            user?.FindFirst("tenant_id")?.Value,
            user?.FindFirst("tenant")?.Value,
            user?.FindFirst("tid")?.Value,
            "tenant-default");

        var resolvedOperatorId = FirstNonEmpty(
            operatorId,
            user?.FindFirst("operator_id")?.Value,
            user?.FindFirst("preferred_username")?.Value,
            user?.FindFirst(ClaimTypes.Name)?.Value,
            user?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            "operator-default");

        return (resolvedTenantId, resolvedOperatorId);
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeHireId(string hireId, out string normalizedHireId, out string error)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            normalizedHireId = string.Empty;
            error = "hireId 不能为空";
            return false;
        }

        normalizedHireId = hireId.Trim();
        error = string.Empty;
        return true;
    }

    private sealed record KingCrewHireRequest(
        string TemplateId,
        string TenantId,
        string OperatorId,
        string? UseCase);

    private sealed record SystemSkillUploadPayload(
        string SkillId,
        string SkillVersion,
        string SkillHash,
        IReadOnlyList<SystemSkillFileUploadPayload> Files,
        IReadOnlyList<SystemSkillStageRuleUploadPayload> StageRules);

    private sealed record SystemSkillFileUploadPayload(
        string RelativePath,
        string ContentHash,
        string Content);

    private sealed record SystemSkillStageRuleUploadPayload(
        string Stage,
        string SkillName,
        string Description,
        IReadOnlyList<string> RequiredFields);

    private sealed record SystemSkillUploadResult(
        string HireId,
        string SandboxId,
        string SkillId,
        string SkillVersion,
        string SkillHash,
        string InstalledPath,
        IReadOnlyList<StageSkillMappingDto> LoadedStageSkills);

    private sealed record TemplatePackageUploadPayload(
        string PackageId,
        string PackageVersion,
        string PackageHash,
        string ManifestJson,
        IReadOnlyList<TemplateOntologySliceUploadPayload> OntologySlices,
        IReadOnlyList<TemplateSkillUploadPayload> RequiredSkills);

    private sealed record TemplateOntologySliceUploadPayload(
        string Name,
        string RelativePath,
        string Type,
        bool Required,
        string ContentHash,
        string Content);

    private sealed record TemplateSkillUploadPayload(
        string Name,
        string RelativePath,
        bool Required,
        string ContentHash,
        string Content);

    private sealed record TemplatePackageUploadResult(
        string HireId,
        string SandboxId,
        string PackageId,
        string PackageVersion,
        string PackageHash,
        string InstalledPath);

    private sealed record HireOwnerContext(
        string OwnerSubject,
        string TenantId,
        string OperatorId,
        string TemplateId,
        string TemplateName,
        string? EmployeeId);

    private sealed record RemoteCallResult<T>(bool Success, int StatusCode, string Message, T? Data)
    {
        public static RemoteCallResult<T> Ok(T data)
        {
            return new RemoteCallResult<T>(true, 200, string.Empty, data);
        }

        public static RemoteCallResult<T> Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "调用下游服务失败" : message;
            return new RemoteCallResult<T>(false, normalizedStatusCode, normalizedMessage, default);
        }
    }
    private sealed record RemoteBinaryCallResult(bool Success, int StatusCode, string Message, string? FileName, string? ContentType, byte[]? Data)
    {
        public static RemoteBinaryCallResult Ok(string fileName, string contentType, byte[] data)
        {
            return new RemoteBinaryCallResult(true, 200, string.Empty, fileName, contentType, data);
        }

        public static RemoteBinaryCallResult Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "璋冪敤涓嬫父鏈嶅姟澶辫触" : message;
            return new RemoteBinaryCallResult(false, normalizedStatusCode, normalizedMessage, null, null, null);
        }
    }
}
