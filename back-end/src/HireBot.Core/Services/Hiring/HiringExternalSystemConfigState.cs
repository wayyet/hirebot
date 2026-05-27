using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Security;

namespace HireBot.Core.Services.Hiring;

internal sealed record HiringExternalSystemConfigState(
    IReadOnlyList<HiringCliToolConfigDto> CliTools,
    HiringMcpServerConfigState? McpServer,
    DateTimeOffset UpdatedAtUtc)
{
    public bool HasAnyConfig =>
        CliTools.Count > 0
        || (McpServer is not null && McpServer.HasAnyConfig);

    public HiringExternalSystemConfigDto ToDto()
        => new()
        {
            CliTools = CliTools,
            McpServer = McpServer?.ToDto(),
            UpdatedAtUtc = UpdatedAtUtc
        };

    public static HiringExternalSystemConfigState FromDto(
        HiringExternalSystemConfigDto? dto,
        HiringExternalSystemConfigState? existingState,
        ISecretProtector secretProtector)
    {
        var normalizedCliTools = (dto?.CliTools ?? [])
            .Select(static tool => NormalizeCliTool(tool))
            .Where(static tool => tool is not null)
            .Select(static tool => tool!)
            .ToArray();

        var normalizedMcpServer = HiringMcpServerConfigState.FromDto(
            dto?.McpServer,
            existingState?.McpServer,
            secretProtector);

        var updatedAtUtc = dto?.UpdatedAtUtc ?? DateTimeOffset.UtcNow;
        return new HiringExternalSystemConfigState(
            normalizedCliTools,
            normalizedMcpServer,
            updatedAtUtc);
    }

    private static HiringCliToolConfigDto? NormalizeCliTool(HiringCliToolConfigDto? tool)
    {
        if (tool is null)
        {
            return null;
        }

        var toolName = tool.ToolName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        var executionMode = string.Equals(tool.ExecutionMode, "sandbox", StringComparison.OrdinalIgnoreCase)
            ? "sandbox"
            : "direct";

        return new HiringCliToolConfigDto
        {
            ToolName = toolName,
            Description = tool.Description?.Trim() ?? string.Empty,
            ExecutionMode = executionMode,
            ArgumentTemplate = tool.ArgumentTemplate?.Trim() ?? string.Empty
        };
    }
}

internal sealed record HiringMcpServerConfigState(
    string ServerUrl,
    string AuthMode,
    string? ProtectedApiKey,
    IReadOnlyList<string> SelectedTools)
{
    public bool HasAnyConfig =>
        !string.IsNullOrWhiteSpace(ServerUrl)
        || SelectedTools.Count > 0
        || !string.IsNullOrWhiteSpace(ProtectedApiKey)
        || !string.Equals(AuthMode, "none", StringComparison.OrdinalIgnoreCase);

    public HiringMcpServerConfigDto ToDto()
        => new()
        {
            ServerUrl = ServerUrl,
            AuthMode = AuthMode,
            ApiKey = string.Empty,
            HasApiKey = !string.IsNullOrWhiteSpace(ProtectedApiKey),
            SelectedTools = SelectedTools
        };

    public static HiringMcpServerConfigState? FromDto(
        HiringMcpServerConfigDto? dto,
        HiringMcpServerConfigState? existingState,
        ISecretProtector secretProtector)
    {
        if (dto is null && existingState is null)
        {
            return null;
        }

        var serverUrl = dto?.ServerUrl?.Trim() ?? existingState?.ServerUrl ?? string.Empty;
        var authMode = NormalizeAuthMode(dto?.AuthMode ?? existingState?.AuthMode);
        var selectedTools = (dto?.SelectedTools ?? existingState?.SelectedTools ?? [])
            .Where(static tool => !string.IsNullOrWhiteSpace(tool))
            .Select(static tool => tool.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var protectedApiKey = ResolveProtectedApiKey(dto, existingState, secretProtector, authMode);
        var normalized = new HiringMcpServerConfigState(serverUrl, authMode, protectedApiKey, selectedTools);
        return normalized.HasAnyConfig ? normalized : null;
    }

    private static string NormalizeAuthMode(string? authMode)
        => authMode?.Trim().ToLowerInvariant() switch
        {
            "api_key" => "api_key",
            "oauth" => "oauth",
            _ => "none"
        };

    private static string? ResolveProtectedApiKey(
        HiringMcpServerConfigDto? dto,
        HiringMcpServerConfigState? existingState,
        ISecretProtector secretProtector,
        string authMode)
    {
        if (!string.Equals(authMode, "api_key", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var apiKey = dto?.ApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return secretProtector.Protect(apiKey);
        }

        return dto?.HasApiKey == true
            ? existingState?.ProtectedApiKey
            : null;
    }
}
