using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Providers;

public sealed class BuildServiceTemplateDataProvider(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<BuildServiceTemplateDataProvider> logger) : ITemplateDataProvider
{
    private const string BuildServiceClientName = "BuildService";
    private const string DefaultApiPrefix = "/api/store";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(BuildServiceClientName);
        if (client.BaseAddress is null)
        {
            logger.LogWarning("BuildService:BaseUrl not configured. Returning empty template list.");
            return [];
        }

        var templates = new List<EmployeeTemplateDefinition>();
        const int pageSize = 100;
        var page = 1;

        while (page <= 20)
        {
            var result = await SendForJsonAsync<BuildTemplateListResponse>(
                client,
                $"/templates?page={page}&pageSize={pageSize}",
                cancellationToken);

            if (!result.Success)
            {
                var message = $"Template list call failed. StatusCode={result.StatusCode}, Message={result.Message}";
                logger.LogWarning(message);
                throw new InvalidOperationException(message);
            }

            if (result.Data?.Items is null || result.Data.Items.Count == 0)
            {
                break;
            }

            templates.AddRange(result.Data.Items.Select(MapListItemToDefinition));

            var fetchedCount = page * pageSize;
            if ((result.Data.Total > 0 && fetchedCount >= result.Data.Total) ||
                result.Data.Items.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return templates;
    }

    public async Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(BuildServiceClientName);
        if (client.BaseAddress is null)
        {
            logger.LogWarning("BuildService:BaseUrl not configured. Unable to load template detail. TemplateId={TemplateId}", templateId);
            return null;
        }

        var result = await SendForJsonAsync<BuildTemplateDetailResponse>(
            client,
            $"/templates/{Uri.EscapeDataString(templateId.Trim())}",
            cancellationToken);

        if (!result.Success)
        {
            if (result.StatusCode == 404)
            {
                return null;
            }

            var message =
                $"Template detail call failed. TemplateId={templateId}, StatusCode={result.StatusCode}, Message={result.Message}";
            logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        return result.Data is null ? null : MapDetailToDefinition(result.Data);
    }

    private async Task<RemoteCallResult<T>> SendForJsonAsync<T>(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(path);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return RemoteCallResult<T>.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(payload) ?? $"Build service call failed (HTTP {(int)response.StatusCode}).");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return RemoteCallResult<T>.Failure(502, "Build service returned an empty body.");
            }

            var model = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (model is null)
            {
                return RemoteCallResult<T>.Failure(502, "Build service response deserialized to null.");
            }

            return RemoteCallResult<T>.Ok(model);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(oce, "Build service request canceled. Path={Path}", path);
            return RemoteCallResult<T>.Failure(499, "Request canceled.");
        }
        catch (OperationCanceledException oce)
        {
            logger.LogWarning(oce, "Build service request timed out. Path={Path}", path);
            return RemoteCallResult<T>.Failure(504, "Build service request timed out.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build service request failed. Path={Path}", path);
            return RemoteCallResult<T>.Failure(502, "Build service request failed.");
        }
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var prefix = configuration["BuildService:ApiPrefix"];
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? DefaultApiPrefix
            : "/" + prefix.Trim().Trim('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        var request = new HttpRequestMessage(HttpMethod.Get, $"{normalizedPrefix}{normalizedPath}");

        var incomingAuthorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(incomingAuthorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", incomingAuthorization);
        }
        else
        {
            var staticToken = configuration["BuildService:BearerToken"];
            if (!string.IsNullOrWhiteSpace(staticToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staticToken.Trim());
            }
            else
            {
                throw new InvalidOperationException(
                    "Missing authorization token for build service. Forward client Authorization header or configure BuildService:BearerToken.");
            }
        }

        return request;
    }

    private static EmployeeTemplateDefinition MapListItemToDefinition(BuildTemplateListItem item)
    {
        var templateId = item.Id.ToString(CultureInfo.InvariantCulture);
        var name = FirstNonEmpty(item.Name, $"Template-{templateId}");
        var positioning = FirstNonEmpty(item.Positioning, item.Description, "Digital employee template");
        var description = FirstNonEmpty(item.Description, item.Positioning, $"{name} template");
        var useCases = ParseUseCases(item.UseCases);
        var coreAbilityTags = useCases.Count > 0 ? useCases : ["General"];
        var coreAbilities = useCases.Count > 0 ? useCases : ["To be configured"];

        return new EmployeeTemplateDefinition(
            TemplateId: templateId,
            IconUrl: BuildDefaultIconUrl(templateId, name),
            Name: name,
            Tagline: positioning,
            Description: description,
            CoreAbilityTags: coreAbilityTags,
            HiredCount: Math.Max(0, item.SkillCount),
            SuccessRate: 0m,
            AvgRating: 0m,
            IsAvailable: string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase),
            CoreAbilities: coreAbilities,
            InScope: useCases,
            OutOfScope: [],
            Prerequisites: [],
            SuccessCases: []);
    }

    private static EmployeeTemplateDefinition MapDetailToDefinition(BuildTemplateDetailResponse detail)
    {
        var templateId = detail.Id.ToString(CultureInfo.InvariantCulture);
        var name = FirstNonEmpty(detail.Name, $"Template-{templateId}");
        var positioning = FirstNonEmpty(detail.Positioning, detail.Description, "Digital employee template");
        var description = FirstNonEmpty(detail.Description, detail.Positioning, $"{name} template");
        var useCases = ParseUseCases(detail.UseCases);
        var coreAbilities = ExtractCoreAbilities(detail.Skills, useCases);
        var coreAbilityTags = BuildCoreAbilityTags(useCases, detail.Skills);
        var inScope = useCases.Count > 0 ? useCases : ExtractOntologyHints(detail.Ontologies);
        if (inScope.Count == 0)
        {
            inScope = ["Execute only within declared template scope"];
        }

        return new EmployeeTemplateDefinition(
            TemplateId: templateId,
            IconUrl: BuildDefaultIconUrl(templateId, name),
            Name: name,
            Tagline: positioning,
            Description: description,
            CoreAbilityTags: coreAbilityTags.Count > 0 ? coreAbilityTags : ["General"],
            HiredCount: ExtractCount(detail.Skills),
            SuccessRate: 0m,
            AvgRating: 0m,
            IsAvailable: string.Equals(detail.Status, "published", StringComparison.OrdinalIgnoreCase),
            CoreAbilities: coreAbilities.Count > 0 ? coreAbilities : ["To be configured"],
            InScope: inScope,
            OutOfScope: [],
            Prerequisites: BuildPrerequisites(detail.Skills, detail.Clis),
            SuccessCases: BuildSuccessCases(detail.LatestVersion));
    }

    private static IReadOnlyList<string> BuildCoreAbilityTags(
        IReadOnlyList<string> useCases,
        JsonElement skills)
    {
        var result = new List<string>(useCases);
        foreach (var skill in EnumerateArray(skills))
        {
            var skillObject = GetProperty(skill, "skill");
            var category = GetString(skillObject, "category");
            if (!string.IsNullOrWhiteSpace(category))
            {
                result.Add(category.Trim());
            }
        }

        return DistinctNonEmpty(result);
    }

    private static IReadOnlyList<string> ExtractCoreAbilities(
        JsonElement skills,
        IReadOnlyList<string> fallback)
    {
        var result = new List<string>();
        foreach (var binding in EnumerateArray(skills))
        {
            var skillObject = GetProperty(binding, "skill");
            var name = FirstNonEmpty(GetString(skillObject, "displayName"), GetString(skillObject, "name"));
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(name);
            }
        }

        if (result.Count == 0)
        {
            result.AddRange(fallback);
        }

        return DistinctNonEmpty(result);
    }

    private static IReadOnlyList<string> ExtractOntologyHints(JsonElement ontologies)
    {
        var result = new List<string>();
        foreach (var ontology in EnumerateArray(ontologies))
        {
            var name = FirstNonEmpty(GetString(ontology, "displayName"), GetString(ontology, "name"));
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add($"Ontology: {name}");
            }
        }

        return DistinctNonEmpty(result);
    }

    private static IReadOnlyList<TemplatePrerequisiteDto> BuildPrerequisites(JsonElement skills, JsonElement clis)
    {
        var result = new List<TemplatePrerequisiteDto>();

        foreach (var binding in EnumerateArray(skills))
        {
            var isRequired = GetBool(binding, "isRequired");
            var level = isRequired ? "required" : "optional";
            var skillObject = GetProperty(binding, "skill");
            var effectiveVersion = GetProperty(binding, "effectiveVersion");
            var systemName = FirstNonEmpty(
                GetString(skillObject, "displayName"),
                GetString(skillObject, "name"),
                "Skill");

            var permissions = DistinctNonEmpty(EnumerateStringArray(GetProperty(effectiveVersion, "permissions")));
            if (permissions.Count > 0)
            {
                foreach (var permission in permissions)
                {
                    result.Add(new TemplatePrerequisiteDto(
                        systemName,
                        permission,
                        level,
                        "Template skill permission dependency"));
                }
            }
            else
            {
                var entryPoint = FirstNonEmpty(
                    GetString(effectiveVersion, "entryPoint"),
                    GetString(effectiveVersion, "version"),
                    "default");
                result.Add(new TemplatePrerequisiteDto(
                    systemName,
                    entryPoint,
                    level,
                    "Template skill runtime dependency"));
            }
        }

        foreach (var cli in EnumerateArray(clis))
        {
            var cliName = FirstNonEmpty(GetString(cli, "displayName"), GetString(cli, "name"));
            if (string.IsNullOrWhiteSpace(cliName))
            {
                continue;
            }

            var version = GetString(cli, "currentVersion");
            var isRequired = GetBool(cli, "isRequired");
            result.Add(new TemplatePrerequisiteDto(
                "CLI",
                cliName,
                isRequired ? "required" : "optional",
                string.IsNullOrWhiteSpace(version) ? "Template CLI dependency" : $"Template CLI dependency ({version})"));
        }

        return result
            .GroupBy(item => $"{item.SystemName}|{item.PermissionName}|{item.RequiredLevel}|{item.Purpose}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<string> BuildSuccessCases(BuildTemplateVersionSnapshot? version)
    {
        if (version is null)
        {
            return [];
        }

        var result = new List<string>();
        var versionText = FirstNonEmpty(version.Version, "unknown");
        if (!string.IsNullOrWhiteSpace(version.ChangeLog))
        {
            result.Add($"Version {versionText}: {version.ChangeLog.Trim()}");
        }

        return DistinctNonEmpty(result);
    }

    private static int ExtractCount(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : default;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName) is var value && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName) is var value &&
               value.ValueKind == JsonValueKind.True;
    }

    private static IReadOnlyList<JsonElement> EnumerateArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray().ToArray();
    }

    private static IReadOnlyList<string> EnumerateStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> DistinctNonEmpty(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseUseCases(JsonElement useCases)
    {
        if (useCases.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (useCases.ValueKind == JsonValueKind.String)
        {
            var raw = useCases.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            var trimmed = raw.Trim();
            if ((trimmed.StartsWith('[') && trimmed.EndsWith(']')) ||
                (trimmed.StartsWith('{') && trimmed.EndsWith('}')))
            {
                try
                {
                    using var document = JsonDocument.Parse(trimmed);
                    return ExtractStringValues(document.RootElement);
                }
                catch
                {
                    // Fallback to delimiter split.
                }
            }

            var splitChars = new[] { ',', ';', '|', '\n' };
            if (trimmed.IndexOfAny(splitChars) >= 0)
            {
                return DistinctNonEmpty(trimmed.Split(splitChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            return [trimmed];
        }

        return ExtractStringValues(useCases);
    }

    private static IReadOnlyList<string> ExtractStringValues(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    values.AddRange(ExtractStringValues(item));
                }
                else if (item.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    values.Add(item.ToString());
                }
            }

            return DistinctNonEmpty(values);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var namedValue = FirstNonEmpty(
                GetString(element, "name"),
                GetString(element, "displayName"),
                GetString(element, "title"),
                GetString(element, "label"),
                GetString(element, "value"));
            if (!string.IsNullOrWhiteSpace(namedValue))
            {
                return [namedValue];
            }

            var nestedValues = new List<string>();
            foreach (var property in element.EnumerateObject())
            {
                nestedValues.AddRange(ExtractStringValues(property.Value));
            }

            return DistinctNonEmpty(nestedValues);
        }

        if (element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
        {
            return [element.ToString()];
        }

        return [];
    }

    private static string BuildDefaultIconUrl(string templateId, string name)
    {
        var text = FirstNonEmpty(name, templateId).ToUpperInvariant();
        var firstGlyph = text.Length > 0 ? text[0].ToString() : "T";
        var background = ResolveColorFromTemplateId(templateId);
        var svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'>" +
            $"<rect width='160' height='160' rx='24' fill='{background}' />" +
            $"<text x='80' y='97' text-anchor='middle' font-size='64' font-family='Arial' fill='white'>{firstGlyph}</text>" +
            "</svg>";
        var encoded = Uri.EscapeDataString(svg);
        return $"data:image/svg+xml,{encoded}";
    }

    private static string ResolveColorFromTemplateId(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return "#334155";
        }

        var seed = 0;
        foreach (var ch in templateId)
        {
            seed += ch;
        }

        var palette = new[]
        {
            "#2563eb",
            "#0f766e",
            "#1d4ed8",
            "#0369a1",
            "#4f46e5",
            "#0891b2"
        };

        return palette[Math.Abs(seed) % palette.Length];
    }

    private static string? ExtractRemoteMessage(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString();
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record BuildTemplateListResponse(
        int Total,
        int Page,
        int PageSize,
        IReadOnlyList<BuildTemplateListItem> Items);

    private sealed record BuildTemplateListItem(
        long Id,
        string? Name,
        string? Positioning,
        string? Description,
        string? CurrentVersion,
        DateTimeOffset? UpdatedAt,
        string? Status,
        JsonElement UseCases,
        int SkillCount,
        int RequiredSkillCount);

    private sealed record BuildTemplateDetailResponse(
        long Id,
        string? Name,
        string? Positioning,
        string? Description,
        string? CurrentVersion,
        DateTimeOffset? UpdatedAt,
        string? Status,
        JsonElement UseCases,
        BuildTemplateVersionSnapshot? LatestVersion,
        JsonElement Skills,
        JsonElement Clis,
        JsonElement Ontologies);

    private sealed record BuildTemplateVersionSnapshot(
        long Id,
        string? Version,
        string? ChangeLog,
        DateTimeOffset? PublishedAt,
        string? PackageUrl);

    private sealed record RemoteCallResult<T>(bool Success, int StatusCode, string Message, T? Data)
    {
        public static RemoteCallResult<T> Ok(T data)
        {
            return new RemoteCallResult<T>(true, 200, string.Empty, data);
        }

        public static RemoteCallResult<T> Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message)
                ? "Build service call failed."
                : message;
            return new RemoteCallResult<T>(false, normalizedStatusCode, normalizedMessage, default);
        }
    }
}
