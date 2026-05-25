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

internal sealed partial class SandboxService
{
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
        var trimmedSandboxId = string.IsNullOrWhiteSpace(request.SandboxId) ? null : request.SandboxId.Trim();
        var hasFullScope = !string.IsNullOrWhiteSpace(request.OwnerSubject) &&
                           !string.IsNullOrWhiteSpace(request.ScopeType) &&
                           !string.IsNullOrWhiteSpace(request.ScopeKey) &&
                           !string.IsNullOrWhiteSpace(request.SandboxRole);

        if (trimmedSandboxId is null && !hasFullScope)
        {
            return null;
        }

        // 优先按 sandboxId 查找；未找到时回退到 scope 查找（sandboxId 可能因自动重建而过期）
        if (trimmedSandboxId is not null)
        {
            var byId = await dbContext.SandboxInstances
                .FirstOrDefaultAsync(item => item.SandboxId == trimmedSandboxId, cancellationToken);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (hasFullScope)
        {
            return await dbContext.SandboxInstances
                .Where(item => item.OwnerSubject == request.OwnerSubject
                    && item.ScopeType == request.ScopeType
                    && item.ScopeKey == request.ScopeKey
                    && item.SandboxRole == request.SandboxRole
                    && item.State != "Deleted")
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    private async Task<SandboxInstanceEntity?> ResolveInstanceForWriteAsync(
        string ownerSubject,
        string scopeType,
        string scopeKey,
        string sandboxRole,
        string? sandboxId,
        CancellationToken cancellationToken)
    {
        var trimmedSandboxId = string.IsNullOrWhiteSpace(sandboxId) ? null : sandboxId.Trim();

        return await dbContext.SandboxInstances
            .Where(item => trimmedSandboxId != null
                ? item.SandboxId == trimmedSandboxId
                : (item.OwnerSubject == ownerSubject && item.ScopeType == scopeType && item.ScopeKey == scopeKey && item.SandboxRole == sandboxRole && item.State != "Deleted"))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

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

    public async Task<SandboxInstanceDto?> FindActiveByOwnerAndTemplateAsync(
        string ownerSubject, string templateId, string sandboxRole, CancellationToken cancellationToken)
    {
        var instance = await dbContext.SandboxInstances
            .Where(item => item.OwnerSubject == ownerSubject
                           && item.TemplateId == templateId
                           && item.SandboxRole == sandboxRole
                           && item.State != "Deleted")
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return instance is null ? null : ToDto(instance);
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
        instance.TemplateId = string.IsNullOrWhiteSpace(request.TemplateId) ? null : request.TemplateId.Trim();
        instance.IsInitialized = request.IsInitialized;
        // Metadata 采用合并语义：请求携带新内容时更新，否则保留已有元数据。
        if (request.Metadata is { Count: > 0 })
        {
            instance.Metadata = new Dictionary<string, string>(request.Metadata);
        }
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
            instance.TemplateId,
            instance.IsInitialized,
            instance.CreatedAtUtc,
            instance.UpdatedAtUtc,
            instance.Metadata);

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

    private static string BuildContentPreview(string? content, int maxLength = 400)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
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

    private SandboxSessionDetailDto MapSessionDetailDto(
        string sessionId,
        SandboxGatewaySessionDetailResponse? response)
    {
        var messages = response?.Session?.History is { Count: > 0 } history
            ? history
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

        logger.LogInformation(
            "MapSessionDetailDto input: SessionId={SessionId}, ResponseIsNull={ResponseIsNull}, HasSession={HasSession}, HasMetadata={HasMetadata}, MetadataHandoffCount={MetadataHandoffCount}, IsActive={IsActive}",
            sessionId,
            response is null,
            response?.Session is not null,
            response?.Metadata is not null,
            response?.Metadata?.HandoffItems?.Count ?? -1,
            response?.IsActive);

        var handoffItems = response?.Metadata?.HandoffItems is { Count: > 0 } metadataHandoffItems
            ? metadataHandoffItems
                .Where(item =>
                {
                    var missingFields = new List<string>();
                    if (string.IsNullOrWhiteSpace(item.SessionId)) missingFields.Add(nameof(item.SessionId));
                    if (string.IsNullOrWhiteSpace(item.WorkflowId)) missingFields.Add(nameof(item.WorkflowId));
                    if (string.IsNullOrWhiteSpace(item.HandoffId)) missingFields.Add(nameof(item.HandoffId));
                    if (string.IsNullOrWhiteSpace(item.Title)) missingFields.Add(nameof(item.Title));
                    if (string.IsNullOrWhiteSpace(item.Kind)) missingFields.Add(nameof(item.Kind));
                    if (string.IsNullOrWhiteSpace(item.Stage)) missingFields.Add(nameof(item.Stage));
                    if (string.IsNullOrWhiteSpace(item.TargetSkill)) missingFields.Add(nameof(item.TargetSkill));
                    if (string.IsNullOrWhiteSpace(item.Status)) missingFields.Add(nameof(item.Status));
                    if (string.IsNullOrWhiteSpace(item.Fingerprint)) missingFields.Add(nameof(item.Fingerprint));
                    if (missingFields.Count > 0)
                    {
                        logger.LogWarning(
                            "Handoff item filtered out (missing fields): HandoffId={HandoffId}, Title={Title}, MissingFields=[{MissingFields}], Stage={Stage}, Status={Status}",
                            item.HandoffId ?? "<null>",
                            item.Title ?? "<null>",
                            string.Join(", ", missingFields),
                            item.Stage ?? "<null>",
                            item.Status ?? "<null>");
                        return false;
                    }
                    return true;
                })
                .Select(item =>
                {
                    var createdAtUtc = item.CreatedAtUtc ?? DateTimeOffset.UtcNow;
                    var updatedAtUtc = item.UpdatedAtUtc ?? createdAtUtc;
                    return new SandboxSessionHandoffItemDto(
                        item.SessionId!.Trim(),
                        item.WorkflowId!.Trim(),
                        item.HandoffId!.Trim(),
                        item.Title!.Trim(),
                        item.Kind!.Trim(),
                        item.Stage!.Trim(),
                        item.TargetSkill!.Trim(),
                        string.IsNullOrWhiteSpace(item.Intent) ? null : item.Intent.Trim(),
                        string.IsNullOrWhiteSpace(item.Category) ? null : item.Category.Trim(),
                        item.Payload is { } payload && payload.ValueKind != JsonValueKind.Undefined ? payload.Clone() : EmptyObject(),
                        string.IsNullOrWhiteSpace(item.Source) ? null : item.Source.Trim(),
                        string.IsNullOrWhiteSpace(item.Acceptance) ? null : item.Acceptance.Trim(),
                        item.Status!.Trim(),
                        item.Fingerprint!.Trim(),
                        item.RelatedTodos?
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray() ?? [],
                        item.RelatedFiles?
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray() ?? [],
                        Math.Max(1, item.Revision ?? 1),
                        createdAtUtc,
                        updatedAtUtc,
                        string.IsNullOrWhiteSpace(item.DispatchId) ? null : item.DispatchId.Trim(),
                        string.IsNullOrWhiteSpace(item.CallbackSummary) ? null : item.CallbackSummary.Trim());
                })
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.HandoffId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        var rawMetadataCount = response?.Metadata?.HandoffItems?.Count ?? 0;
        logger.LogInformation(
            "MapSessionDetailDto handoff filter: RawMetadataCount={RawCount}, PassedFilter={PassedCount}, SessionId={SessionId}",
            rawMetadataCount,
            handoffItems.Length,
            sessionId);

        return new SandboxSessionDetailDto(
            sessionId,
            messages,
            handoffItems,
            response?.IsActive ?? false);
    }

    private sealed record SandboxGatewaySessionDetailResponse(
        SandboxGatewaySessionDetail? Session,
        SandboxGatewaySessionMetadata? Metadata,
        bool IsActive);

    private sealed record SandboxGatewaySessionDetail(
        string Id,
        IReadOnlyList<SandboxGatewaySessionTurn> History);

    private sealed record SandboxGatewaySessionMetadata(
        IReadOnlyList<SandboxGatewaySessionHandoffItem> HandoffItems);

    private sealed record SandboxGatewaySessionTurn(
        string Role,
        string? Content,
        DateTimeOffset? Timestamp);

    public async Task<ApiResponse<DigitalEmployeeTemplateUploadResultDto>> UploadDigitalEmployeeTemplateAsync(
        DigitalEmployeeTemplateUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SandboxId))
            return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.ErrorResponse(400, "sandboxId 不能为空");
        if (request.ArchiveBytes is null || request.ArchiveBytes.Length == 0)
            return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.ErrorResponse(400, "archive bytes 不能为空");

        var instance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == request.SandboxId, cancellationToken);
        if (instance is null)
            return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.ErrorResponse(404, "sandbox instance not found");

        var refreshResult = await provisioner.RefreshAsync(request.SandboxId, cancellationToken);
        if (refreshResult.State is not "Running")
            return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.ErrorResponse(409, $"sandbox not ready (state={refreshResult.State})");
        if (string.IsNullOrWhiteSpace(refreshResult.GatewayEndpoint))
            return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.ErrorResponse(409, "sandbox gateway endpoint missing");

        instance.State = refreshResult.State;
        instance.GatewayEndpoint = refreshResult.GatewayEndpoint;
        await dbContext.SaveChangesAsync(cancellationToken);

        var call = await kingCrabHttpClient.SendMultipartForJsonAsync<SkillPackageUploadResponse>(
            "/admin/digital-employee/upload",
            "file",
            request.FileName,
            request.ArchiveBytes,
            "application/zip",
            request.OwnerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: refreshResult.GatewayEndpoint);

        if (!call.Success || call.Data is null)
            return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.ErrorResponse(call.StatusCode, call.Message);

        return ApiResponse<DigitalEmployeeTemplateUploadResultDto>.SuccessResponse(
            new DigitalEmployeeTemplateUploadResultDto(
                call.Data.Success,
                call.Data.Error,
                call.Data.SkillsInstalled),
            "skill package uploaded");
    }

    private sealed record SkillPackageUploadResponse(
        bool Success,
        string? Error,
        string? Name,
        int SkillsInstalled);

    private sealed record SandboxGatewaySessionHandoffItem(
        [property: JsonPropertyName("session_id")] string? SessionId,
        [property: JsonPropertyName("workflow_id")] string? WorkflowId,
        [property: JsonPropertyName("handoff_id")] string? HandoffId,
        string? Title,
        string? Kind,
        string? Stage,
        [property: JsonPropertyName("target_skill")] string? TargetSkill,
        string? Intent,
        string? Category,
        JsonElement? Payload,
        string? Source,
        string? Acceptance,
        string? Status,
        string? Fingerprint,
        [property: JsonPropertyName("related_todos")] IReadOnlyList<string>? RelatedTodos,
        [property: JsonPropertyName("related_files")] IReadOnlyList<string>? RelatedFiles,
        int? Revision,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAtUtc,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAtUtc,
        [property: JsonPropertyName("dispatch_id")] string? DispatchId,
        [property: JsonPropertyName("callback_summary")] string? CallbackSummary);

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private sealed record AttachmentPayload(string FileName, string ContentType, byte[] Content, string ContentHash, string? StoragePath);
}



