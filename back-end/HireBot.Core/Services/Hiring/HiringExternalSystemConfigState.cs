using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Security;

namespace HireBot.Core.Services.Hiring;

internal sealed record HiringExternalSystemConfigState(
    string SubmissionMode,
    IReadOnlyList<HiringCliToolConfigState> CliTools,
    HiringMcpServerConfigState? McpServer,
    DateTimeOffset UpdatedAtUtc)
{
    public bool IsSkipped => string.Equals(SubmissionMode, HiringExternalSystemSubmissionModes.Skipped, StringComparison.OrdinalIgnoreCase);

    public bool HasConfiguredSystems =>
        CliTools.Count > 0
        || (McpServer is not null && McpServer.HasAnyConfig);

    public bool IsPersisted => IsSkipped || HasConfiguredSystems;

    public HiringExternalSystemConfigDto ToDto(ISecretProtector secretProtector)
        => new()
        {
            SubmissionMode = ResolveSubmissionMode(this),
            CliTools = CliTools
                .Select(static tool => tool.ToDto())
                .ToArray(),
            McpServer = McpServer?.ToDto(secretProtector),
            UpdatedAtUtc = UpdatedAtUtc
        };

    public static HiringExternalSystemConfigState FromDto(
        HiringExternalSystemConfigDto? dto,
        ISecretProtector secretProtector)
    {
        var normalizedCliTools = (dto?.CliTools ?? [])
            .Select(static tool => HiringCliToolConfigState.FromDto(tool))
            .Where(static tool => tool is not null)
            .Select(static tool => tool!)
            .ToArray();

        var normalizedMcpServer = HiringMcpServerConfigState.FromDto(dto?.McpServer, secretProtector);
        var provisionalState = new HiringExternalSystemConfigState(
            SubmissionMode: HiringExternalSystemSubmissionModes.Pending,
            CliTools: normalizedCliTools,
            McpServer: normalizedMcpServer,
            UpdatedAtUtc: dto?.UpdatedAtUtc ?? DateTimeOffset.UtcNow);

        return provisionalState with
        {
            SubmissionMode = NormalizeSubmissionMode(dto?.SubmissionMode, provisionalState)
        };
    }

    private static string NormalizeSubmissionMode(string? submissionMode, HiringExternalSystemConfigState state)
    {
        if (string.Equals(submissionMode, HiringExternalSystemSubmissionModes.Skipped, StringComparison.OrdinalIgnoreCase))
        {
            return HiringExternalSystemSubmissionModes.Skipped;
        }

        return state.HasConfiguredSystems
            ? HiringExternalSystemSubmissionModes.Configured
            : HiringExternalSystemSubmissionModes.Pending;
    }

    private static string ResolveSubmissionMode(HiringExternalSystemConfigState state)
    {
        if (state.IsSkipped)
        {
            return HiringExternalSystemSubmissionModes.Skipped;
        }

        return state.HasConfiguredSystems
            ? HiringExternalSystemSubmissionModes.Configured
            : HiringExternalSystemSubmissionModes.Pending;
    }
}

internal sealed record HiringCliToolConfigState(
    string Name,
    string Command,
    string Description,
    string ExecutionMode,
    JsonElement? Parameters)
{
    public HiringCliToolConfigDto ToDto()
        => new()
        {
            Name = Name,
            Command = Command,
            Description = Description,
            ExecutionMode = ExecutionMode,
            Parameters = CloneParameters(Parameters)
        };

    public static HiringCliToolConfigState? FromDto(HiringCliToolConfigDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var name = dto.Name?.Trim() ?? string.Empty;
        var command = dto.Command?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var executionMode = string.Equals(dto.ExecutionMode, "sandbox", StringComparison.OrdinalIgnoreCase)
            ? "sandbox"
            : "direct";

        return new HiringCliToolConfigState(
            Name: name,
            Command: command,
            Description: dto.Description?.Trim() ?? string.Empty,
            ExecutionMode: executionMode,
            Parameters: CloneParameters(dto.Parameters));
    }

    private static JsonElement? CloneParameters(JsonElement? parameters)
    {
        if (!parameters.HasValue)
        {
            return null;
        }

        return parameters.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : parameters.Value.Clone();
    }
}

internal sealed record HiringMcpServerConfigState(
    string Transport,
    string Name,
    string Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> ProtectedEnv,
    IReadOnlyList<string> EnvPassThrough,
    string Cwd,
    string Url,
    string BearerTokenEnv,
    IReadOnlyDictionary<string, string> ProtectedHeaders,
    IReadOnlyDictionary<string, string> HeadersFromEnv)
{
    // SSE 和 Streamable HTTP 均基于 URL，仅校验名称与 URL
    public bool HasAnyConfig =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Url);

    public HiringMcpServerConfigDto ToDto(ISecretProtector secretProtector)
        => new()
        {
            Transport = Transport,
            Name = Name,
            Command = Command,
            Args = Args,
            Env = UnprotectMap(ProtectedEnv, secretProtector),
            EnvPassThrough = EnvPassThrough,
            Cwd = Cwd,
            Url = Url,
            BearerTokenEnv = BearerTokenEnv,
            Headers = UnprotectMap(ProtectedHeaders, secretProtector),
            HeadersFromEnv = HeadersFromEnv
        };

    public static HiringMcpServerConfigState? FromDto(
        HiringMcpServerConfigDto? dto,
        ISecretProtector secretProtector)
    {
        if (dto is null)
        {
            return null;
        }

        var transport = NormalizeTransport(dto.Transport);
        var name = dto.Name?.Trim() ?? string.Empty;
        // SSE 和 Streamable HTTP 均基于 URL，不再支持 stdio 本地进程模式
        var url = dto.Url?.Trim() ?? string.Empty;
        var normalized = new HiringMcpServerConfigState(
            Transport: transport,
            Name: name,
            Command: string.Empty,
            Args: [],
            ProtectedEnv: EmptyMap(),
            EnvPassThrough: [],
            Cwd: string.Empty,
            Url: url,
            BearerTokenEnv: dto.BearerTokenEnv?.Trim() ?? string.Empty,
            ProtectedHeaders: ProtectMap(dto.Headers, secretProtector),
            HeadersFromEnv: NormalizeMap(dto.HeadersFromEnv));

        return normalized.HasAnyConfig ? normalized : null;
    }

    private static string NormalizeTransport(string? transport)
        => transport?.ToLowerInvariant() switch
        {
            "sse" => "sse",
            // http 是旧别名，stdio 为遗留传输，均规范化为 streamable-http
            "streamable-http" or "http" or "stdio" => "streamable-http",
            _ => "streamable-http",
        };

    private static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values)
        => (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, string> NormalizeMap(IReadOnlyDictionary<string, string>? values)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return map;
        }

        foreach (var (key, value) in values)
        {
            var normalizedKey = key?.Trim() ?? string.Empty;
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(normalizedValue))
            {
                continue;
            }

            map[normalizedKey] = normalizedValue;
        }

        return map;
    }

    private static IReadOnlyDictionary<string, string> ProtectMap(
        IReadOnlyDictionary<string, string>? values,
        ISecretProtector secretProtector)
    {
        var protectedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in NormalizeMap(values))
        {
            var protectedValue = secretProtector.Protect(value);
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                throw new InvalidOperationException($"External config secret protection failed for key '{key}'.");
            }

            protectedValues[key] = protectedValue;
        }

        return protectedValues;
    }

    private static IReadOnlyDictionary<string, string> UnprotectMap(
        IReadOnlyDictionary<string, string> values,
        ISecretProtector secretProtector)
    {
        var unprotectedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var unprotectedValue = secretProtector.Unprotect(value);
            if (string.IsNullOrWhiteSpace(unprotectedValue))
            {
                continue;
            }

            unprotectedValues[key] = unprotectedValue;
        }

        return unprotectedValues;
    }

    private static IReadOnlyDictionary<string, string> EmptyMap()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
