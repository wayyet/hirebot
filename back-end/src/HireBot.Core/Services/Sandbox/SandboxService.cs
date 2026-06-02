using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Sandbox;

internal sealed partial class SandboxService(
    HireBotDbContext dbContext,
    OpenSandboxProvisioner provisioner,
    IKingCrabHttpClient kingCrabHttpClient,
    KingCrabGatewayClient gatewayClient,
    ILogger<SandboxService> logger) : ISandboxService
{
    private const string StableSessionHeader = "X-OpenClaw-Session-Id";

    public async Task<ApiResponse<SandboxInstanceDto>> RegisterAsync(
        SandboxRegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(400, validationMessage);
        }

        if (string.IsNullOrWhiteSpace(request.SandboxId))
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(400, "sandboxId 不能为空");
        }

        var instance = await dbContext.SandboxInstances
            .Where(item => item.SandboxId == request.SandboxId.Trim() ||
                          (item.OwnerSubject == request.OwnerSubject && item.ScopeType == request.ScopeType && item.ScopeKey == request.ScopeKey && item.SandboxRole == request.SandboxRole && item.State != "Deleted"))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance is null)
        {
            instance = new SandboxInstanceEntity();
            dbContext.SandboxInstances.Add(instance);
        }

        PopulateInstance(instance, request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
    }

    public async Task<ApiResponse<SandboxInstanceDto>> CreateAsync(
        SandboxCreateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(400, validationMessage);
        }

        if (!string.Equals(request.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(400, "CreateAsync 仅支持 managed 模式");
        }

        var provisioned = await provisioner.CreateAsync(request.OwnerSubject.Trim(), request.ScopeKey ?? string.Empty, request.SandboxRole, cancellationToken);
        var instance = await FindInstanceByScopeAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey ?? string.Empty, request.SandboxRole, cancellationToken);
        if (instance is null)
        {
            instance = new SandboxInstanceEntity();
            dbContext.SandboxInstances.Add(instance);
        }

        PopulateInstance(instance, new SandboxRegisterRequestDto
        {
            SandboxId = provisioned.SandboxId,
            ScopeType = request.ScopeType,
            ScopeKey = request.ScopeKey,
            SandboxRole = request.SandboxRole,
            OwnerSubject = request.OwnerSubject,
            TenantId = request.TenantId,
            OperatorId = request.OperatorId,
            ProvisioningMode = request.ProvisioningMode,
            State = provisioned.State,
            GatewayEndpoint = provisioned.GatewayEndpoint,
            ExpiresAtUtc = provisioned.ExpiresAtUtc,
            UseCase = request.UseCase,
            TemplateId = request.TemplateId,
            Metadata = request.Metadata
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await provisioner.BeginTrackingAsync(instance.Id, provisioned.SandboxId);
        return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
    }

    public async Task<ApiResponse<SandboxInstanceDto>> RefreshAsync(
        SandboxInstanceLookupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var instance = await ResolveInstanceAsync(request, cancellationToken);
        if (instance is null)
        {
            if (string.IsNullOrWhiteSpace(request.OwnerSubject))
            {
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(400, "Cannot create sandbox: OwnerSubject is required.");
            }

            logger.LogWarning("Sandbox instance not found, auto-creating. ScopeType={ScopeType}, ScopeKey={ScopeKey}, SandboxRole={SandboxRole}, OwnerSubject={OwnerSubject}",
                request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject);

            var provisioned = await provisioner.CreateAsync(request.OwnerSubject.Trim(), request.ScopeKey ?? string.Empty, request.SandboxRole ?? string.Empty, cancellationToken);
            instance = new SandboxInstanceEntity();
            dbContext.SandboxInstances.Add(instance);
            PopulateInstance(instance, new SandboxRegisterRequestDto
            {
                SandboxId = provisioned.SandboxId,
                ScopeType = request.ScopeType ?? string.Empty,
                ScopeKey = request.ScopeKey ?? string.Empty,
                SandboxRole = request.SandboxRole ?? string.Empty,
                OwnerSubject = request.OwnerSubject,
                TenantId = request.TenantId ?? string.Empty,
                OperatorId = request.OperatorId ?? string.Empty,
                ProvisioningMode = "managed",
                State = provisioned.State,
                GatewayEndpoint = provisioned.GatewayEndpoint,
                ExpiresAtUtc = provisioned.ExpiresAtUtc,
                UseCase = request.UseCase,
                TemplateId = request.TemplateId,
                IsInitialized = false
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await provisioner.BeginTrackingAsync(instance.Id, provisioned.SandboxId);
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
        }

        if (!string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
        }

        var refreshed = await provisioner.RefreshAsync(instance.SandboxId, cancellationToken);

        if (string.Equals(refreshed.State, "NotFound", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Sandbox not found, recreating. OldSandboxId={OldSandboxId}, OwnerSubject={OwnerSubject}",
                instance.SandboxId, instance.OwnerSubject);

            var provisioned = await provisioner.CreateAsync(instance.OwnerSubject, instance.ScopeKey, instance.SandboxRole, cancellationToken);
            instance.SandboxId = provisioned.SandboxId;
            instance.State = provisioned.State;
            instance.GatewayEndpoint = provisioned.GatewayEndpoint;
            instance.ExpiresAtUtc = provisioned.ExpiresAtUtc;
            instance.LastError = null;
            instance.IsInitialized = false;
            instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await provisioner.BeginTrackingAsync(instance.Id, provisioned.SandboxId);
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
        }

        instance.State = refreshed.State;
        instance.GatewayEndpoint = refreshed.GatewayEndpoint;
        instance.ExpiresAtUtc = refreshed.ExpiresAtUtc;
        instance.LastError = null;
        instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
    }

    public Task<ApiResponse<SandboxInstanceDto>> PauseAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
        => ChangeManagedStateAsync(request, "Paused", static (p, id, ct) => p.PauseAsync(id, ct), cancellationToken);

    public Task<ApiResponse<SandboxInstanceDto>> ResumeAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken = default)
        => ChangeManagedStateAsync(request, "Running", static (p, id, ct) => p.ResumeAsync(id, ct), cancellationToken);

    public async Task<ApiResponse<SandboxInstanceDto>> RebuildAsync(
        SandboxInstanceLookupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var instance = await ResolveInstanceAsync(request, cancellationToken);
        if (instance is null)
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(404, "Sandbox not found.");
        }

        if (!string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(409, "Current sandbox was not provisioned by HireBot and cannot be rebuilt.");
        }

        var rebuilt = await provisioner.RebuildAsync(instance.OwnerSubject, instance.SandboxId, instance.ScopeKey, instance.SandboxRole, cancellationToken);
        instance.SandboxId = rebuilt.SandboxId;
        instance.State = rebuilt.State;
        instance.GatewayEndpoint = rebuilt.GatewayEndpoint;
        instance.ExpiresAtUtc = rebuilt.ExpiresAtUtc;
        instance.LastError = null;
        instance.IsInitialized = false;
        instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await provisioner.BeginTrackingAsync(instance.Id, rebuilt.SandboxId);
        return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
    }

    public async Task<ApiResponse<bool>> DeleteAsync(
        SandboxInstanceLookupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var instance = await ResolveInstanceAsync(request, cancellationToken);
        if (instance is null)
        {
            return ApiResponse<bool>.ErrorResponse(404, "Sandbox not found.");
        }

        if (string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            await provisioner.DeleteAsync(instance.SandboxId, instance.ScopeKey, cancellationToken);
        }

        instance.State = "Deleted";
        instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<IReadOnlyList<SandboxInstanceDto>>> ListByOwnerAsync(
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return ApiResponse<IReadOnlyList<SandboxInstanceDto>>.ErrorResponse(400, "ownerSubject 不能为空");
        }

        var items = await dbContext.SandboxInstances
            .Where(item => item.OwnerSubject == ownerSubject.Trim() && item.State != "Deleted")
            .OrderByDescending(item => item.UpdatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        IReadOnlyList<SandboxInstanceDto> result = items.ConvertAll(ToDto);
        return ApiResponse<IReadOnlyList<SandboxInstanceDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<bool>> DeleteForOwnerAsync(
        string sandboxId,
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sandboxId) || string.IsNullOrWhiteSpace(ownerSubject))
        {
            return ApiResponse<bool>.ErrorResponse(400, "sandboxId 和 ownerSubject 不能为空");
        }

        var instance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == sandboxId.Trim() && item.State != "Deleted", cancellationToken);

        if (instance is null)
        {
            return ApiResponse<bool>.ErrorResponse(404, "沙箱不存在");
        }

        // 校验归属关系，防止越权删除
        if (!string.Equals(instance.OwnerSubject, ownerSubject.Trim(), StringComparison.Ordinal))
        {
            return ApiResponse<bool>.ErrorResponse(403, "无权操作该沙箱");
        }

        if (string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            await provisioner.DeleteAsync(instance.SandboxId, instance.ScopeKey, cancellationToken);
        }

        instance.State = "Deleted";
        instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<StartHiringConversationResultDto>> EnsureSessionAsync(
        SandboxEnsureSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(400, validationMessage);
        }
        if (!string.Equals(request.ScopeType, SandboxScopeTypes.Hire, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(501, "Only hire scope message sending is supported.");
        }
        var instance = await ResolveInstanceForWriteAsync(
            request.OwnerSubject,
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SandboxId,
            cancellationToken);
        if (instance is null)
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(404, "Sandbox not found.");
        }
        var gatewayEndpointResult = await ResolveGatewayEndpointResultAsync(instance, cancellationToken);
        if (!gatewayEndpointResult.Success)
        {
            return ApiResponse<StartHiringConversationResultDto>.ErrorResponse(gatewayEndpointResult.StatusCode, gatewayEndpointResult.Message);
        }
        var sessionId = request.SessionId?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var existingSession = await FindSessionAsync(
                request.OwnerSubject,
                request.ScopeType,
                request.ScopeKey,
                request.SandboxRole,
                request.SessionKey,
                cancellationToken);
            sessionId = string.IsNullOrWhiteSpace(existingSession?.SessionId)
                ? $"session-{Guid.NewGuid():N}"
                : existingSession!.SessionId;
        }
        await UpsertSessionEntityAsync(
            request.OwnerSubject,
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SessionKey,
            sessionId,
            request.SandboxId ?? instance.SandboxId,
            cancellationToken);
        return ApiResponse<StartHiringConversationResultDto>.SuccessResponse(
            new StartHiringConversationResultDto(
                request.ScopeKey.Trim(),
                sessionId,
                ResolveDefaultStage(request.SandboxRole),
                false,
                []));
    }

    public async Task<ApiResponse<HiringConversationResultDto>> SendMessageAsync(
        SandboxSendMessageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(400, validationMessage);
        }
        if (!string.Equals(request.ScopeType, SandboxScopeTypes.Hire, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(501, "Only hire scope message sending is supported.");
        }

        logger.LogInformation(
            "Sending sandbox message. ScopeType={ScopeType}, ScopeKey={ScopeKey}, SandboxRole={SandboxRole}, SessionKey={SessionKey}, SandboxId={SandboxId}, UploadMaterialsAsAttachments={UploadMaterialsAsAttachments}, MaterialCount={MaterialCount}",
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SessionKey,
            request.SandboxId,
            request.UploadMaterialsAsAttachments,
            request.Materials?.Count ?? 0);

        var content = request.Content?.Trim() ?? string.Empty;
        if (request.UploadMaterialsAsAttachments && request.Materials is not null)
        {
            var markers = new List<string>();
            foreach (var material in request.Materials.Where(ShouldUploadMaterial))
            {
                var uploadResult = await UploadAttachmentAsync(
                    new SandboxAttachmentUploadRequestDto
                    {
                        ScopeType = request.ScopeType,
                        ScopeKey = request.ScopeKey,
                        SandboxRole = request.SandboxRole,
                        OwnerSubject = request.OwnerSubject,
                        TenantId = request.TenantId,
                        OperatorId = request.OperatorId,
                        SessionKey = request.SessionKey,
                        SandboxId = request.SandboxId,
                        Material = material
                    },
                    cancellationToken);
                if (!uploadResult.Success || uploadResult.Data is null)
                {
                    return ApiResponse<HiringConversationResultDto>.ErrorResponse(uploadResult.Code, uploadResult.Message);
                }

                logger.LogInformation(
                    "Sandbox attachment uploaded and converted to marker. ScopeKey={ScopeKey}, SessionKey={SessionKey}, MaterialName={MaterialName}, MaterialType={MaterialType}, MimeType={MimeType}, MediaId={MediaId}, MediaUrl={MediaUrl}, Marker={Marker}",
                    request.ScopeKey,
                    request.SessionKey,
                    material.Name,
                    material.Type,
                    material.MimeType,
                    uploadResult.Data.MediaId,
                    uploadResult.Data.Url,
                    uploadResult.Data.Marker);

                markers.Add(uploadResult.Data.Marker);
            }
            if (markers.Count > 0)
            {
                content = BuildContentWithMarkers(content, markers);

                logger.LogInformation(
                    "Sandbox message markers injected into outbound content. ScopeKey={ScopeKey}, SessionKey={SessionKey}, MarkerCount={MarkerCount}, Markers={Markers}, ContentLength={ContentLength}, ContentPreview={ContentPreview}",
                    request.ScopeKey,
                    request.SessionKey,
                    markers.Count,
                    string.Join(", ", markers),
                    content.Length,
                    BuildContentPreview(content));
            }
        }
        var outboundRequest = new HiringConversationMessageRequestDto
        {
            Content = content,
            StructuredAnswers = request.StructuredAnswers,
            Materials = request.Materials
        };
        var instance = await ResolveInstanceForWriteAsync(
            request.OwnerSubject,
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SandboxId,
            cancellationToken);
        if (instance is null)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(404, "Sandbox not found.");
        }
        var ensureSessionResult = await EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = request.ScopeType,
                ScopeKey = request.ScopeKey,
                SandboxRole = request.SandboxRole,
                OwnerSubject = request.OwnerSubject,
                TenantId = request.TenantId,
                OperatorId = request.OperatorId,
                SessionKey = request.SessionKey,
                SandboxId = request.SandboxId ?? instance.SandboxId
            },
            cancellationToken);
        if (!ensureSessionResult.Success || ensureSessionResult.Data is null || string.IsNullOrWhiteSpace(ensureSessionResult.Data.SessionId))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(ensureSessionResult.Code, ensureSessionResult.Message);
        }
        var gatewayEndpoint = instance.GatewayEndpoint?.Trim();
        if (string.IsNullOrWhiteSpace(gatewayEndpoint))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(409, "Sandbox gateway endpoint is not ready.");
        }
        var sessionId = ensureSessionResult.Data.SessionId.Trim();
        logger.LogInformation(
            "Dispatching sandbox chat completion request. ScopeKey={ScopeKey}, SessionId={SessionId}, GatewayEndpoint={GatewayEndpoint}, ContentLength={ContentLength}, ContentPreview={ContentPreview}",
            request.ScopeKey,
            sessionId,
            gatewayEndpoint,
            outboundRequest.Content?.Length ?? 0,
            BuildContentPreview(outboundRequest.Content));

        var gatewayCall = await kingCrabHttpClient.SendForJsonAsync<SandboxGatewayChatCompletionResponse>(
            HttpMethod.Post,
            "/v1/chat/completions",
            new SandboxGatewayChatCompletionRequest(
                Model: null,
                Messages:
                [
                    new SandboxGatewayChatMessage("user", outboundRequest.Content ?? string.Empty)
                ],
                Stream: false),
            request.OwnerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: gatewayEndpoint,
            additionalHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [StableSessionHeader] = sessionId
            });
        if (!gatewayCall.Success || gatewayCall.Data is null)
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(gatewayCall.StatusCode, gatewayCall.Message);
        }
        var assistantContent = gatewayCall.Data.Choices
            .FirstOrDefault()?
            .Message?
            .Content?
            .Trim();
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return ApiResponse<HiringConversationResultDto>.ErrorResponse(502, "Sandbox conversation returned an empty response.");
        }
        var now = DateTimeOffset.UtcNow;
        var previewStructuredData = NormalizeStructuredAnswers(request.StructuredAnswers);
        return ApiResponse<HiringConversationResultDto>.SuccessResponse(
            new HiringConversationResultDto(
                request.ScopeKey.Trim(),
                sessionId,
                ResolveDefaultStage(request.SandboxRole),
                false,
                new HiringConversationMessageDto(
                    $"assistant-{Guid.NewGuid():N}",
                    "assistant",
                    assistantContent,
                    now),
                new HiringStagePreviewDto(
                    request.ScopeKey.Trim(),
                    ResolveDefaultStage(request.SandboxRole),
                    request.SandboxRole.Trim(),
                    BuildPreviewSummary(assistantContent),
                    previewStructuredData,
                    [],
                    [],
                    false,
                    now)));
    }

    public async Task<ApiResponse<HiringConversationTimelineDto>> GetTimelineAsync(
        SandboxTimelineRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(400, validationMessage);
        }

        if (!string.Equals(request.ScopeType, SandboxScopeTypes.Hire, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(501, "当前仅支持 hire scope 的时间线查询");
        }

        var instance = await ResolveInstanceForWriteAsync(
            request.OwnerSubject,
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SandboxId,
            cancellationToken);
        if (instance is null)
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(404, "Sandbox not found.");
        }

        if (string.IsNullOrWhiteSpace(instance.SandboxId))
        {
            return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(409, "Sandbox id is not ready.");
        }

        {
            var ensureSessionResult = await EnsureSessionAsync(
                new SandboxEnsureSessionRequestDto
                {
                    ScopeType = request.ScopeType,
                    ScopeKey = request.ScopeKey,
                    SandboxRole = request.SandboxRole,
                    OwnerSubject = request.OwnerSubject,
                    TenantId = request.TenantId,
                    OperatorId = request.OperatorId,
                    SessionKey = request.SessionKey,
                    SandboxId = request.SandboxId ?? instance.SandboxId
                },
                cancellationToken);
            if (!ensureSessionResult.Success || ensureSessionResult.Data is null || string.IsNullOrWhiteSpace(ensureSessionResult.Data.SessionId))
            {
                return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(ensureSessionResult.Code, ensureSessionResult.Message);
            }

            var sessionId = ensureSessionResult.Data.SessionId.Trim();
            var timelineGatewayEndpointResult = await provisioner.GetGatewayEndpointResultAsync(instance.SandboxId, useServerProxy: false, cancellationToken);
            if (!timelineGatewayEndpointResult.Success || string.IsNullOrWhiteSpace(timelineGatewayEndpointResult.Data))
            {
                return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(
                    timelineGatewayEndpointResult.StatusCode,
                    timelineGatewayEndpointResult.Message);
            }

            var timelineGatewayEndpoint = timelineGatewayEndpointResult.Data;

            var gatewayCall = await kingCrabHttpClient.SendForJsonAsync<SandboxGatewaySessionDetailResponse>(
                HttpMethod.Get,
                $"/api/integration/sessions/{Uri.EscapeDataString(sessionId)}",
                body: null,
                request.OwnerSubject,
                cancellationToken,
                useHireBotApiPrefix: false,
                absoluteBaseUrl: timelineGatewayEndpoint);

            if (!gatewayCall.Success && gatewayCall.StatusCode != (int)HttpStatusCode.NotFound)
            {
                return ApiResponse<HiringConversationTimelineDto>.ErrorResponse(gatewayCall.StatusCode, gatewayCall.Message);
            }

            var messages = gatewayCall.Success && gatewayCall.Data?.Session is not null
                ? gatewayCall.Data.Session.History
                    .Where(item =>
                        string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    .Select((item, index) => new HiringConversationMessageDto(
                        $"{sessionId}:{index + 1}",
                        item.Role,
                        item.Content ?? string.Empty,
                        item.Timestamp ?? DateTimeOffset.UtcNow))
                    .ToArray()
                : [];

            await UpsertSessionEntityAsync(
                request.OwnerSubject,
                request.ScopeType,
                request.ScopeKey,
                request.SandboxRole,
                request.SessionKey,
                sessionId,
                request.SandboxId ?? instance.SandboxId,
                cancellationToken);

            return ApiResponse<HiringConversationTimelineDto>.SuccessResponse(
                new HiringConversationTimelineDto(
                    request.ScopeKey.Trim(),
                    sessionId,
                    ResolveDefaultStage(request.SandboxRole),
                    false,
                    messages.Length == 0 ? HiringCollectionPhase.NotStarted : HiringCollectionPhase.InProgress,
                    messages,
                    []));
        }

    }

    public async Task<ApiResponse<SandboxSessionDetailDto>> GetSessionDetailAsync(
        SandboxSessionDetailRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(400, validationMessage);
        }

        if (!string.Equals(request.ScopeType, SandboxScopeTypes.Hire, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(501, "当前仅支持 hire scope 的会话明细查询");
        }

        var instance = await ResolveInstanceForWriteAsync(
            request.OwnerSubject,
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SandboxId,
            cancellationToken);
        if (instance is null)
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(404, "Sandbox not found.");
        }

        if (string.IsNullOrWhiteSpace(instance.SandboxId))
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(409, "Sandbox id is not ready.");
        }

        var ensureSessionResult = await EnsureSessionAsync(
            new SandboxEnsureSessionRequestDto
            {
                ScopeType = request.ScopeType,
                ScopeKey = request.ScopeKey,
                SandboxRole = request.SandboxRole,
                OwnerSubject = request.OwnerSubject,
                TenantId = request.TenantId,
                OperatorId = request.OperatorId,
                SessionKey = request.SessionKey,
                SandboxId = request.SandboxId ?? instance.SandboxId
            },
            cancellationToken);
        if (!ensureSessionResult.Success || ensureSessionResult.Data is null || string.IsNullOrWhiteSpace(ensureSessionResult.Data.SessionId))
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(ensureSessionResult.Code, ensureSessionResult.Message);
        }

        var sessionId = ensureSessionResult.Data.SessionId.Trim();
        var gatewayEndpointResult = await provisioner.GetGatewayEndpointResultAsync(instance.SandboxId, useServerProxy: false, cancellationToken);
        if (!gatewayEndpointResult.Success || string.IsNullOrWhiteSpace(gatewayEndpointResult.Data))
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(
                gatewayEndpointResult.StatusCode,
                gatewayEndpointResult.Message);
        }

        var gatewayCall = await kingCrabHttpClient.SendForJsonAsync<SandboxGatewaySessionDetailResponse>(
            HttpMethod.Get,
            $"/api/integration/sessions/{Uri.EscapeDataString(sessionId)}",
            body: null,
            request.OwnerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: gatewayEndpointResult.Data);

        if (!gatewayCall.Success && gatewayCall.StatusCode != (int)HttpStatusCode.NotFound)
        {
            return ApiResponse<SandboxSessionDetailDto>.ErrorResponse(gatewayCall.StatusCode, gatewayCall.Message);
        }

        await UpsertSessionEntityAsync(
            request.OwnerSubject,
            request.ScopeType,
            request.ScopeKey,
            request.SandboxRole,
            request.SessionKey,
            sessionId,
            request.SandboxId ?? instance.SandboxId,
            cancellationToken);

        var rawHandoffCount = gatewayCall.Success ? gatewayCall.Data?.Metadata?.HandoffItems?.Count ?? 0 : 0;
        logger.LogInformation(
            "GetSessionDetail gateway call: Success={Success}, StatusCode={StatusCode}, SessionId={SessionId}, RawHandoffItems={RawHandoffCount}, DataIsNull={DataIsNull}, MetadataIsNull={MetadataIsNull}, IsActive={IsActive}",
            gatewayCall.Success,
            gatewayCall.StatusCode,
            sessionId,
            rawHandoffCount,
            gatewayCall.Data is null,
            gatewayCall.Data?.Metadata is null,
            gatewayCall.Data?.IsActive);

        var detail = MapSessionDetailDto(sessionId, gatewayCall.Success ? gatewayCall.Data : null);
        logger.LogInformation(
            "GetSessionDetail result: SessionId={SessionId}, Messages={MessageCount}, HandoffItems={HandoffCount}, IsActive={IsActive}",
            detail.SessionId,
            detail.Messages.Count,
            detail.HandoffItems.Count,
            detail.IsActive);

        return ApiResponse<SandboxSessionDetailDto>.SuccessResponse(detail);
    }

    public async Task<ApiResponse<SandboxAttachmentUploadResultDto>> UploadAttachmentAsync(
        SandboxAttachmentUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
        {
            return ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(400, validationMessage);
        }
        if (request.Material is null)
        {
            return ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(400, "material 不能为空");
        }
        var payloadResult = await BuildAttachmentPayloadAsync(request.Material, cancellationToken);
        if (!payloadResult.Success || payloadResult.Data is null)
        {
            return ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(payloadResult.Code, payloadResult.Message);
        }
        var targetBaseUrlResult = await ResolveGatewayEndpointResultAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, request.SandboxId, cancellationToken);
        if (!targetBaseUrlResult.Success || string.IsNullOrWhiteSpace(targetBaseUrlResult.Data))
        {
            return ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(targetBaseUrlResult.StatusCode, targetBaseUrlResult.Message);
        }
        var uploadCall = await gatewayClient.UploadMediaAsync(request.OwnerSubject, payloadResult.Data.FileName, payloadResult.Data.Content, payloadResult.Data.ContentType, cancellationToken, targetBaseUrlResult.Data);
        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return ApiResponse<SandboxAttachmentUploadResultDto>.ErrorResponse(uploadCall.StatusCode, uploadCall.Message);
        }
        var session = await FindSessionAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, request.SessionKey, cancellationToken);
        var instance = await ResolveInstanceForWriteAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, request.SandboxId, cancellationToken);
        var asset = new SandboxAssetEntity
        {
            SandboxInstanceEntityId = instance?.Id,
            SandboxSessionEntityId = session?.Id,
            MediaId = uploadCall.Data.MediaId,
            Url = uploadCall.Data.Url,
            FileName = uploadCall.Data.FileName,
            MimeType = uploadCall.Data.MimeType,
            SizeBytes = uploadCall.Data.SizeBytes,
            ContentHash = payloadResult.Data.ContentHash,
            StoragePath = payloadResult.Data.StoragePath,
            AssetRole = request.Material.Type
        };
        dbContext.SandboxAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Sandbox attachment persisted. ScopeKey={ScopeKey}, SessionKey={SessionKey}, AssetId={AssetId}, MediaId={MediaId}, MediaUrl={MediaUrl}, StoragePath={StoragePath}, FileName={FileName}, MimeType={MimeType}, SizeBytes={SizeBytes}",
            request.ScopeKey,
            request.SessionKey,
            asset.Id,
            asset.MediaId,
            asset.Url,
            asset.StoragePath,
            asset.FileName,
            asset.MimeType,
            asset.SizeBytes);

        return ApiResponse<SandboxAttachmentUploadResultDto>.SuccessResponse(new SandboxAttachmentUploadResultDto(
            asset.Id,
            asset.SandboxInstanceEntityId,
            asset.SandboxSessionEntityId,
            asset.MediaId,
            asset.Url,
            asset.FileName,
            asset.MimeType,
            asset.SizeBytes,
            asset.ContentHash,
            asset.StoragePath,
            uploadCall.Data.Marker,
            asset.CreatedAtUtc));
    }

    public async Task<ApiResponse<SandboxWorkspaceUploadResultDto>> UploadWorkspaceFileAsync(
        SandboxWorkspaceUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateScope(request.ScopeType, request.ScopeKey, request.SandboxRole, request.OwnerSubject, out var validationMessage))
            return ApiResponse<SandboxWorkspaceUploadResultDto>.ErrorResponse(400, validationMessage);

        if (string.IsNullOrWhiteSpace(request.TargetDir))
            return ApiResponse<SandboxWorkspaceUploadResultDto>.ErrorResponse(400, "targetDir 不能为空");

        if (request.Content is not { Length: > 0 })
            return ApiResponse<SandboxWorkspaceUploadResultDto>.ErrorResponse(400, "content 不能为空");

        var targetBaseUrlResult = await ResolveGatewayEndpointResultAsync(
            request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, request.SandboxId, cancellationToken);
        if (!targetBaseUrlResult.Success || string.IsNullOrWhiteSpace(targetBaseUrlResult.Data))
            return ApiResponse<SandboxWorkspaceUploadResultDto>.ErrorResponse(targetBaseUrlResult.StatusCode, targetBaseUrlResult.Message);

        var uploadCall = await gatewayClient.UploadToWorkspaceAsync(
            request.OwnerSubject,
            request.FileName,
            request.Content,
            request.ContentType,
            request.TargetDir,
            cancellationToken,
            targetBaseUrlResult.Data);

        if (!uploadCall.Success || uploadCall.Data is null)
            return ApiResponse<SandboxWorkspaceUploadResultDto>.ErrorResponse(uploadCall.StatusCode, uploadCall.Message);

        var workspaceDir = $"/workspace/{request.TargetDir.Trim('/')}";
        logger.LogInformation(
            "Sandbox workspace upload completed. ScopeKey={ScopeKey}, TargetDir={TargetDir}, WorkspaceDir={WorkspaceDir}, FileCount={FileCount}",
            request.ScopeKey, request.TargetDir, workspaceDir, uploadCall.Data.FileCount);

        return ApiResponse<SandboxWorkspaceUploadResultDto>.SuccessResponse(
            new SandboxWorkspaceUploadResultDto(uploadCall.Data.Files, uploadCall.Data.FileCount, workspaceDir));
    }

    private async Task<ApiResponse<SandboxInstanceDto>> ChangeManagedStateAsync(
        SandboxInstanceLookupRequestDto request,
        string newState,
        Func<OpenSandboxProvisioner, string, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var instance = await ResolveInstanceAsync(request, cancellationToken);
        if (instance is null)
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(404, "Sandbox not found.");
        }

        if (!string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(409, "Current sandbox was not provisioned by HireBot and cannot change state.");
        }

        await action(provisioner, instance.SandboxId, cancellationToken);
        instance.State = newState;
        instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
    }

}
