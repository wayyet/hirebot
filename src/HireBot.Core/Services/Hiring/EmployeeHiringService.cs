using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

public sealed class EmployeeHiringService(
    ITemplateDataProvider templateDataProvider,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
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

        if (request is null || string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(400, "tenantId 和 operatorId 为必填项");
        }

        var normalizedTemplateId = templateId.Trim();
        var template = await templateDataProvider.GetByIdAsync(normalizedTemplateId, cancellationToken);
        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<HireTemplateResultDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        var ownerSubject = ResolveOwnerSubject(request.TenantId, request.OperatorId);
        var remoteRequest = new KingCrewHireRequest(
            TemplateId: normalizedTemplateId,
            TenantId: request.TenantId.Trim(),
            OperatorId: request.OperatorId.Trim(),
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

        hireOwners[call.Data.HireId] = new HireOwnerContext(
            OwnerSubject: ownerSubject,
            TenantId: request.TenantId.Trim(),
            OperatorId: request.OperatorId.Trim());

        logger.LogInformation(
            "模板雇佣已提交到 KingCrew: HireId={HireId}, TemplateId={TemplateId}, Owner={Owner}",
            call.Data.HireId,
            normalizedTemplateId,
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

        if (request is null || (string.IsNullOrWhiteSpace(request.Content) && (request.StructuredAnswers is null || request.StructuredAnswers.Count == 0)))
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

        return ApiResponse<HiringFinalizeResultDto>.SuccessResponse(call.Data, "交付物已生成");
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

        return ApiResponse<HiringWorkflowStateDto>.SuccessResponse(call.Data);
    }

    public async Task<HiringArtifactDownloadResult> BuildArtifactDownloadAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return HiringArtifactDownloadResult.Error(400, error);
        }

        var client = httpClientFactory.CreateClient(KingCrewClientName);
        if (client.BaseAddress is null)
        {
            return HiringArtifactDownloadResult.Error(500, "KingCrew:BaseUrl 未配置");
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"/hirings/{Uri.EscapeDataString(normalizedHireId)}/artifacts/download",
            ResolveOwnerByHireId(normalizedHireId));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return HiringArtifactDownloadResult.Error(
                (int)response.StatusCode,
                ExtractRemoteMessage(errorContent) ?? $"下载交付包失败（HTTP {(int)response.StatusCode}）");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            return HiringArtifactDownloadResult.Error(502, "下载交付包失败：返回内容为空");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fileName.Trim('"');
        }

        return HiringArtifactDownloadResult.Success(
            fileName ?? $"{normalizedHireId}_artifacts.zip",
            string.IsNullOrWhiteSpace(contentType) ? "application/zip" : contentType,
            bytes);
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
        catch (Exception ex)
        {
            logger.LogError(ex, "调用 KingCrew 接口异常: Method={Method}, Path={Path}", method, path);
            return RemoteCallResult<T>.Failure(502, "调用 KingCrew 接口异常");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string ownerSubject)
    {
        var prefix = configuration["KingCrew:HireBotApiPrefix"];
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? DefaultHireBotApiPrefix
            : "/" + prefix.Trim().Trim('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        var request = new HttpRequestMessage(method, $"{normalizedPrefix}{normalizedPath}");

        // 优先透传前端携带的 Authorization，便于 OIDC 身份在下游延续。
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

    private string ResolveOwnerSubject(string tenantId, string operatorId)
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

        return $"{tenantId.Trim()}:{operatorId.Trim()}";
    }

    private string ResolveOwnerByHireId(string hireId)
    {
        if (hireOwners.TryGetValue(hireId, out var context))
        {
            return context.OwnerSubject;
        }

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

        return "anonymous";
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

    private sealed record KingCrewHireRequest(string TemplateId, string TenantId, string OperatorId, string? UseCase);
    private sealed record HireOwnerContext(string OwnerSubject, string TenantId, string OperatorId);

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
}
