using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Services.EmployeeRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class KingCrewRuntimeChatClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<KingCrewRuntimeChatClient> logger) : IKingCrewRuntimeChatClient
{
    private const string KingCrewClientName = "KingCrew";
    private const string DefaultRuntimeChatPath = "/api/integration/hirebot/runtime-chat/messages";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse<RuntimeChatResponseDto>> SendAsync(
        RuntimeChatRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (configuration.GetValue("KingCrew:EnableLocalSimulation", false))
        {
            var lastUserMessage = request.Messages.LastOrDefault(item =>
                string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase));
            return ApiResponse<RuntimeChatResponseDto>.SuccessResponse(
                new RuntimeChatResponseDto($"已收到：{lastUserMessage?.Content ?? string.Empty}"));
        }

        var client = httpClientFactory.CreateClient(KingCrewClientName);
        if (client.BaseAddress is null)
        {
            return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(500, "KingCrew:BaseUrl 未配置");
        }

        var path = configuration["KingCrew:RuntimeChatPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultRuntimeChatPath;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, NormalizePath(path));
        var token = configuration["KingCrew:BearerToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(content) ?? $"调用 KingCrew runtime chat 失败（HTTP {(int)response.StatusCode}）");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(502, "KingCrew runtime chat 响应为空");
            }

            var result = JsonSerializer.Deserialize<RuntimeChatResponseDto>(content, JsonOptions);
            if (result is null || string.IsNullOrWhiteSpace(result.Content))
            {
                var extracted = ExtractAssistantContent(content);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    result = new RuntimeChatResponseDto(extracted);
                }
            }

            if (result is null || string.IsNullOrWhiteSpace(result.Content))
            {
                return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(502, "KingCrew runtime chat 响应解析为空");
            }

            return ApiResponse<RuntimeChatResponseDto>.SuccessResponse(result);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "KingCrew runtime chat request canceled. InstanceId={InstanceId}", request.InstanceId);
            return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(499, "请求已取消");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "KingCrew runtime chat timeout. InstanceId={InstanceId}", request.InstanceId);
            return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(504, "调用 KingCrew runtime chat 超时");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "KingCrew runtime chat failed. InstanceId={InstanceId}", request.InstanceId);
            return ApiResponse<RuntimeChatResponseDto>.ErrorResponse(502, "调用 KingCrew runtime chat 异常");
        }
    }

    private static string NormalizePath(string path)
    {
        return "/" + path.Trim().TrimStart('/');
    }

    private static string? ExtractRemoteMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }
        }
        catch (JsonException)
        {
            return content.Length > 500 ? content[..500] : content;
        }

        return null;
    }

    private static string? ExtractAssistantContent(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            foreach (var name in new[] { "content", "reply", "message", "assistant_message", "assistantMessage" })
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "content", "reply", "message", "assistant_message", "assistantMessage" })
                {
                    if (data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
