using System.Net;
using System.Security.Cryptography;
using System.Text;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Sandbox;

internal sealed class SandboxService(
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

        var instance = await FindInstanceBySandboxIdAsync(request.SandboxId.Trim(), cancellationToken)
            ?? await FindInstanceByScopeAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, cancellationToken);

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

        var provisioned = await provisioner.CreateAsync(request.OwnerSubject.Trim(), cancellationToken);
        var instance = await FindInstanceByScopeAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, cancellationToken);
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
            UseCase = request.UseCase
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
            return ApiResponse<SandboxInstanceDto>.ErrorResponse(404, "Sandbox not found.");
        }

        if (!string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(ToDto(instance));
        }

        var refreshed = await provisioner.RefreshAsync(instance.SandboxId, cancellationToken);
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

        var rebuilt = await provisioner.RebuildAsync(instance.OwnerSubject, instance.SandboxId, cancellationToken);
        instance.SandboxId = rebuilt.SandboxId;
        instance.State = rebuilt.State;
        instance.GatewayEndpoint = rebuilt.GatewayEndpoint;
        instance.ExpiresAtUtc = rebuilt.ExpiresAtUtc;
        instance.LastError = null;
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
            await provisioner.DeleteAsync(instance.SandboxId, cancellationToken);
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
                markers.Add(uploadResult.Data.Marker);
            }
            if (markers.Count > 0)
            {
                content = BuildContentWithMarkers(content, markers);
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
        var gatewayCall = await kingCrabHttpClient.SendForJsonAsync<SandboxGatewayChatCompletionResponse>(
            HttpMethod.Post,
            "/v1/chat/completions",
            new SandboxGatewayChatCompletionRequest(
                Model: null,
                Messages:
                [
                    new SandboxGatewayChatMessage("user", outboundRequest.Content)
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

    private static bool ValidateScope(string scopeType, string scopeKey, string sandboxRole, string ownerSubject, out string message)
    {
        if (string.IsNullOrWhiteSpace(scopeType))
        {
            message = "scopeType 不能为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            message = "scopeKey 不能为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sandboxRole))
        {
            message = "sandboxRole 不能为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            message = "ownerSubject 不能为空";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private async Task<SandboxInstanceEntity?> ResolveInstanceAsync(SandboxInstanceLookupRequestDto request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SandboxId))
        {
            return await FindInstanceBySandboxIdAsync(request.SandboxId.Trim(), cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.OwnerSubject) ||
            string.IsNullOrWhiteSpace(request.ScopeType) ||
            string.IsNullOrWhiteSpace(request.ScopeKey) ||
            string.IsNullOrWhiteSpace(request.SandboxRole))
        {
            return null;
        }

        return await FindInstanceByScopeAsync(request.OwnerSubject, request.ScopeType, request.ScopeKey, request.SandboxRole, cancellationToken);
    }

    private async Task<SandboxInstanceEntity?> ResolveInstanceForWriteAsync(
        string ownerSubject,
        string scopeType,
        string scopeKey,
        string sandboxRole,
        string? sandboxId,
        CancellationToken cancellationToken)
    {
        SandboxInstanceEntity? instance = null;
        if (!string.IsNullOrWhiteSpace(sandboxId))
        {
            instance = await FindInstanceBySandboxIdAsync(sandboxId.Trim(), cancellationToken);
        }

        return instance ?? await FindInstanceByScopeAsync(ownerSubject, scopeType, scopeKey, sandboxRole, cancellationToken);
    }

    private Task<SandboxInstanceEntity?> FindInstanceBySandboxIdAsync(string sandboxId, CancellationToken cancellationToken)
        => dbContext.SandboxInstances.FirstOrDefaultAsync(item => item.SandboxId == sandboxId, cancellationToken);

    private Task<SandboxInstanceEntity?> FindInstanceByScopeAsync(string ownerSubject, string scopeType, string scopeKey, string sandboxRole, CancellationToken cancellationToken)
        => dbContext.SandboxInstances
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(item => item.OwnerSubject == ownerSubject && item.ScopeType == scopeType && item.ScopeKey == scopeKey && item.SandboxRole == sandboxRole && item.State != "Deleted", cancellationToken);

    private Task<SandboxSessionEntity?> FindSessionAsync(string ownerSubject, string scopeType, string scopeKey, string sandboxRole, string sessionKey, CancellationToken cancellationToken)
        => dbContext.SandboxSessions.FirstOrDefaultAsync(item => item.OwnerSubject == ownerSubject && item.ScopeType == scopeType && item.ScopeKey == scopeKey && item.SandboxRole == sandboxRole && item.SessionKey == sessionKey, cancellationToken);

    private async Task UpsertSessionEntityAsync(string ownerSubject, string scopeType, string scopeKey, string sandboxRole, string sessionKey, string sessionId, string? sandboxId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(ownerSubject, scopeType, scopeKey, sandboxRole, sessionKey, cancellationToken);
        var instance = await ResolveInstanceForWriteAsync(ownerSubject, scopeType, scopeKey, sandboxRole, sandboxId, cancellationToken);

        if (session is null)
        {
            session = new SandboxSessionEntity
            {
                OwnerSubject = ownerSubject,
                ScopeType = scopeType,
                ScopeKey = scopeKey,
                SandboxRole = sandboxRole,
                SessionKey = sessionKey
            };
            dbContext.SandboxSessions.Add(session);
        }

        session.SessionId = sessionId.Trim();
        session.SandboxInstanceEntityId = instance?.Id;
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void PopulateInstance(SandboxInstanceEntity instance, SandboxRegisterRequestDto request)
    {
        instance.SandboxId = request.SandboxId.Trim();
        instance.ScopeType = request.ScopeType.Trim();
        instance.ScopeKey = request.ScopeKey.Trim();
        instance.SandboxRole = request.SandboxRole.Trim();
        instance.ProvisioningMode = request.ProvisioningMode.Trim();
        instance.OwnerSubject = request.OwnerSubject.Trim();
        instance.TenantId = request.TenantId.Trim();
        instance.OperatorId = request.OperatorId.Trim();
        instance.State = request.State.Trim();
        instance.GatewayEndpoint = string.IsNullOrWhiteSpace(request.GatewayEndpoint) ? null : request.GatewayEndpoint.Trim();
        instance.ExpiresAtUtc = request.ExpiresAtUtc;
        instance.UseCase = string.IsNullOrWhiteSpace(request.UseCase) ? null : request.UseCase.Trim();
        instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static SandboxInstanceDto ToDto(SandboxInstanceEntity instance)
        => new(
            instance.Id,
            instance.SandboxId,
            instance.ScopeType,
            instance.ScopeKey,
            instance.SandboxRole,
            instance.ProvisioningMode,
            instance.OwnerSubject,
            instance.TenantId,
            instance.OperatorId,
            instance.State,
            instance.GatewayEndpoint,
            instance.ExpiresAtUtc,
            instance.LastError,
            instance.UseCase,
            instance.CreatedAtUtc,
            instance.UpdatedAtUtc);

    private async Task<RemoteCallResult<string>> ResolveGatewayEndpointResultAsync(
        string ownerSubject,
        string scopeType,
        string scopeKey,
        string sandboxRole,
        string? sandboxId,
        CancellationToken cancellationToken)
    {
        var instance = await ResolveInstanceForWriteAsync(ownerSubject, scopeType, scopeKey, sandboxRole, sandboxId, cancellationToken);
        if (instance is null)
        {
            return RemoteCallResult<string>.Failure(404, "Sandbox not found.");
        }
        return await ResolveGatewayEndpointResultAsync(instance, cancellationToken);
    }
    private async Task<RemoteCallResult<string>> ResolveGatewayEndpointResultAsync(
        SandboxInstanceEntity instance,
        CancellationToken cancellationToken)
    {
        if (string.Equals(instance.ProvisioningMode, "managed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(instance.SandboxId))
            {
                return RemoteCallResult<string>.Failure(409, "Sandbox id is not ready.");
            }
            var gatewayEndpointResult = await provisioner.GetGatewayEndpointResultAsync(instance.SandboxId, useServerProxy: false, cancellationToken);
            if (!gatewayEndpointResult.Success || string.IsNullOrWhiteSpace(gatewayEndpointResult.Data))
            {
                return RemoteCallResult<string>.Failure(gatewayEndpointResult.StatusCode, gatewayEndpointResult.Message);
            }
            var gatewayEndpoint = gatewayEndpointResult.Data.Trim();
            if (!string.Equals(instance.GatewayEndpoint, gatewayEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                instance.GatewayEndpoint = gatewayEndpoint;
                instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return RemoteCallResult<string>.Ok(gatewayEndpoint);
        }
        if (string.IsNullOrWhiteSpace(instance.GatewayEndpoint))
        {
            return RemoteCallResult<string>.Failure(409, "Sandbox gateway endpoint is not ready.");
        }
        return RemoteCallResult<string>.Ok(instance.GatewayEndpoint.Trim());
    }

    private static string ResolveDefaultStage(string sandboxRole)
    {
        return sandboxRole.Contains("evaluation", StringComparison.OrdinalIgnoreCase)
            ? "evaluation"
            : HiringCollectionStage.Material;
    }

    private static Dictionary<string, string?> NormalizeStructuredAnswers(IReadOnlyDictionary<string, string>? structuredAnswers)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (structuredAnswers is null)
        {
            return result;
        }

        foreach (var pair in structuredAnswers)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            result[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return result;
    }

    private static string BuildPreviewSummary(string assistantContent)
    {
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return "Sandbox conversation completed.";
        }

        var normalized = assistantContent.Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180];
    }

    private static bool ShouldUploadMaterial(HiringConversationMaterialDto material)
    {
        if (material.Metadata?.ContainsKey("storagePath") == true)
        {
            return true;
        }

        if ((material.Metadata?.TryGetValue("contentEncoding", out var encoding) == true ||
             material.Metadata?.TryGetValue("encoding", out encoding) == true) &&
            string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(material.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(material.MimeType) &&
            !material.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string BuildContentWithMarkers(string content, IReadOnlyList<string> markers)
    {
        var markerBlock = string.Join(Environment.NewLine, markers);
        return string.IsNullOrWhiteSpace(content)
            ? markerBlock
            : $"{content.Trim()}{Environment.NewLine}{Environment.NewLine}{markerBlock}";
    }

    private async Task<ApiResponse<AttachmentPayload>> BuildAttachmentPayloadAsync(HiringConversationMaterialDto material, CancellationToken cancellationToken)
    {
        var fileName = string.IsNullOrWhiteSpace(material.Name) ? "attachment.bin" : material.Name.Trim();
        var contentType = string.IsNullOrWhiteSpace(material.MimeType) ? "application/octet-stream" : material.MimeType.Trim();

        if (material.Metadata?.TryGetValue("storagePath", out var storagePath) == true && !string.IsNullOrWhiteSpace(storagePath))
        {
            if (!File.Exists(storagePath))
            {
                return ApiResponse<AttachmentPayload>.ErrorResponse(422, $"附件文件不存在: {storagePath}");
            }

            var bytes = await File.ReadAllBytesAsync(storagePath, cancellationToken);
            return ApiResponse<AttachmentPayload>.SuccessResponse(new AttachmentPayload(fileName, contentType, bytes, material.ContentHash ?? Convert.ToHexStringLower(SHA256.HashData(bytes)), storagePath));
        }

        if (string.IsNullOrWhiteSpace(material.Content))
        {
            return ApiResponse<AttachmentPayload>.ErrorResponse(422, $"Attachment {fileName} is missing upload content.");
        }

        byte[] contentBytes;
        if ((material.Metadata?.TryGetValue("contentEncoding", out var encoding) == true ||
             material.Metadata?.TryGetValue("encoding", out encoding) == true) &&
            string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                contentBytes = Convert.FromBase64String(material.Content.Trim());
            }
            catch (FormatException)
            {
                return ApiResponse<AttachmentPayload>.ErrorResponse(422, $"Attachment {fileName} has invalid base64 content.");
            }
        }
        else
        {
            contentBytes = Encoding.UTF8.GetBytes(material.Content);
        }

        return ApiResponse<AttachmentPayload>.SuccessResponse(new AttachmentPayload(fileName, contentType, contentBytes, material.ContentHash ?? Convert.ToHexStringLower(SHA256.HashData(contentBytes)), null));
    }

    private sealed record SandboxGatewayChatCompletionRequest(
        string? Model,
        IReadOnlyList<SandboxGatewayChatMessage> Messages,
        bool Stream);

    private sealed record SandboxGatewayChatMessage(
        string Role,
        string Content);

    private sealed record SandboxGatewayChatCompletionResponse(
        IReadOnlyList<SandboxGatewayChatCompletionChoice> Choices);

    private sealed record SandboxGatewayChatCompletionChoice(
        SandboxGatewayChatMessage? Message);

    private sealed record SandboxGatewaySessionDetailResponse(
        SandboxGatewaySessionDetail? Session,
        bool IsActive);

    private sealed record SandboxGatewaySessionDetail(
        string Id,
        IReadOnlyList<SandboxGatewaySessionTurn> History);

    private sealed record SandboxGatewaySessionTurn(
        string Role,
        string? Content,
        DateTimeOffset? Timestamp);

    private sealed record AttachmentPayload(string FileName, string ContentType, byte[] Content, string ContentHash, string? StoragePath);
}



