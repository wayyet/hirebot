using System.Globalization;
using System.Text.Json;

namespace HireBot.Core.Providers;

internal static class BuildServiceTemplatePayloadParser
{
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    public static BuildTemplateListPage ParseListPage(JsonElement payload)
    {
        var root = UnwrapEnvelope(payload);
        var itemsElement = FindProperty(root, "items", "records", "list", "templates", "rows");
        if (itemsElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null && root.ValueKind == JsonValueKind.Array)
        {
            itemsElement = root;
        }

        var items = EnumerateArray(itemsElement)
            .Select(ParseTemplate)
            .Where(template => template is not null)
            .Cast<BuildTemplateDocument>()
            .ToArray();

        var total = GetInt(root, "total", "totalCount", "count", "totalRecords") ?? items.Length;
        return new BuildTemplateListPage(total, items);
    }

    public static BuildTemplateDocument? ParseDetail(JsonElement payload)
    {
        var root = UnwrapEnvelope(payload);
        return ParseTemplate(root);
    }

    private static BuildTemplateDocument? ParseTemplate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var latestVersion = ParseVersion(ResolveVersionElement(element));
        var templateId = FirstNonEmpty(
            GetString(element, "id", "templateId", "template_id"),
            GetString(FindProperty(element, "template"), "id", "templateId", "template_id"));
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        var skills = FindArrayOrEmpty(element, "skills", "skillBindings", "skill_bindings", "requiredSkills");
        var ontologies = FindArrayOrEmpty(element, "ontologies", "ontologyBindings", "ontology_bindings", "ontologySlices", "ontology_slices");
        var clis = FindArrayOrEmpty(element, "clis", "cliBindings", "cli_bindings", "cliTools", "cli_tools");
        var useCases = FindProperty(element, "useCases", "use_cases", "scenarios", "scenarioTags", "domains");
        if (useCases.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            useCases = FindProperty(element, "tags");
        }

        var currentVersion = FirstNonEmpty(
            GetString(element, "currentVersion", "current_version"),
            latestVersion?.Version);
        var skillCount = GetInt(element, "skillCount", "skillsCount", "totalSkillCount") ?? skills.GetArrayLength();
        var requiredSkillCount = GetInt(element, "requiredSkillCount", "requiredSkillsCount") ?? CountRequiredSkills(skills);
        var hiredCount = GetInt(element, "hiredCount", "hireCount", "usageCount", "usedCount") ?? Math.Max(skillCount, requiredSkillCount);
        var status = ResolveStatus(element);

        return new BuildTemplateDocument(
            TemplateId: templateId,
            Name: FirstNonEmpty(
                GetString(element, "name", "displayName", "display_name", "templateName", "title"),
                GetString(FindProperty(element, "template"), "name", "displayName", "display_name", "templateName", "title")),
            Positioning: FirstNonEmpty(
                GetString(element, "positioning", "tagline", "subtitle", "summary"),
                GetString(FindProperty(element, "template"), "positioning", "tagline", "subtitle", "summary")),
            Description: FirstNonEmpty(
                GetString(element, "description", "desc", "introduction", "summary"),
                GetString(FindProperty(element, "template"), "description", "desc", "introduction", "summary")),
            CurrentVersion: currentVersion,
            UpdatedAt: GetDateTimeOffset(element, "updatedAt", "lastUpdatedAt", "modifiedAt"),
            Status: status,
            UseCases: CloneOrEmpty(useCases),
            LatestVersion: latestVersion,
            Skills: CloneOrEmpty(skills),
            Clis: CloneOrEmpty(clis),
            Ontologies: CloneOrEmpty(ontologies),
            SkillCount: Math.Max(0, skillCount),
            RequiredSkillCount: Math.Max(0, requiredSkillCount),
            HiredCount: Math.Max(0, hiredCount));
    }

    private static BuildTemplateVersionSnapshot? ParseVersion(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var version = FirstNonEmpty(
            GetString(element, "version", "versionName", "versionNo", "versionNumber", "currentVersion"),
            GetString(FindProperty(element, "package"), "version", "versionName", "versionNo", "versionNumber"));
        var package = FindProperty(element, "package", "artifact", "file");
        var packageUrl = FirstNonEmpty(
            GetString(element, "packageUrl", "assetUrl", "downloadUrl", "fileUrl", "url"),
            GetString(package, "packageUrl", "assetUrl", "downloadUrl", "fileUrl", "url"));
        if (string.IsNullOrWhiteSpace(version) && string.IsNullOrWhiteSpace(packageUrl))
        {
            return null;
        }

        return new BuildTemplateVersionSnapshot(
            Id: GetString(element, "id"),
            Version: version,
            ChangeLog: FirstNonEmpty(
                GetString(element, "changeLog", "changelog", "releaseNotes", "releaseNote", "description"),
                GetString(package, "changeLog", "changelog", "releaseNotes", "releaseNote", "description")),
            PublishedAt: GetDateTimeOffset(element, "publishedAt", "updatedAt", "createdAt", "releaseAt"),
            PackageUrl: packageUrl);
    }

    private static JsonElement ResolveVersionElement(JsonElement element)
    {
        var version = FindProperty(
            element,
            "latestVersion",
            "latestPublishedVersion",
            "publishedVersion",
            "currentVersionInfo",
            "versionInfo");
        if (version.ValueKind == JsonValueKind.Object)
        {
            return version;
        }

        var versions = FindProperty(element, "templateVersions", "versions", "publishedVersions");
        if (versions.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        var selected = EnumerateArray(versions)
            .FirstOrDefault(item =>
                string.Equals(GetString(item, "status"), "published", StringComparison.OrdinalIgnoreCase) ||
                GetBool(item, "isPublished", "published"));
        if (selected.ValueKind == JsonValueKind.Object)
        {
            return selected;
        }

        return EnumerateArray(versions).FirstOrDefault();
    }

    private static string ResolveStatus(JsonElement element)
    {
        var status = FirstNonEmpty(
            GetString(element, "status"),
            GetString(FindProperty(element, "template"), "status"));
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        if (GetBool(element, "isPublished", "published") ||
            GetBool(FindProperty(element, "template"), "isPublished", "published"))
        {
            return "published";
        }

        return string.Empty;
    }

    private static int CountRequiredSkills(JsonElement skills)
    {
        return EnumerateArray(skills)
            .Count(skill => GetBool(skill, "isRequired", "required"));
    }

    private static JsonElement UnwrapEnvelope(JsonElement payload)
    {
        var current = payload;
        while (current.ValueKind == JsonValueKind.Object)
        {
            var data = FindProperty(current, "data", "result");
            if (data.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                current = data;
                continue;
            }

            break;
        }

        return current;
    }

    private static JsonElement FindArrayOrEmpty(JsonElement element, params string[] propertyNames)
    {
        var candidate = FindProperty(element, propertyNames);
        return candidate.ValueKind == JsonValueKind.Array ? candidate : EmptyArray;
    }

    private static JsonElement CloneOrEmpty(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? EmptyArray
            : element.Clone();
    }

    private static JsonElement FindProperty(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                return value;
            }
        }

        return default;
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        if (propertyNames.Length > 0)
        {
            element = FindProperty(element, propertyNames);
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static bool GetBool(JsonElement element, params string[] propertyNames)
    {
        if (propertyNames.Length > 0)
        {
            element = FindProperty(element, propertyNames);
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var result) && result,
            JsonValueKind.Number => element.TryGetInt32(out var number) && number != 0,
            _ => false
        };
    }

    private static int? GetInt(JsonElement element, params string[] propertyNames)
    {
        if (propertyNames.Length > 0)
        {
            element = FindProperty(element, propertyNames);
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => (int)Math.Clamp(longValue, int.MinValue, int.MaxValue),
            JsonValueKind.String when int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, params string[] propertyNames)
    {
        var raw = GetString(element, propertyNames);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    private static IReadOnlyList<JsonElement> EnumerateArray(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().ToArray()
            : [];
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
}

internal sealed record BuildTemplateListPage(
    int Total,
    IReadOnlyList<BuildTemplateDocument> Items);

internal sealed record BuildTemplateDocument(
    string TemplateId,
    string Name,
    string Positioning,
    string Description,
    string CurrentVersion,
    DateTimeOffset? UpdatedAt,
    string Status,
    JsonElement UseCases,
    BuildTemplateVersionSnapshot? LatestVersion,
    JsonElement Skills,
    JsonElement Clis,
    JsonElement Ontologies,
    int SkillCount,
    int RequiredSkillCount,
    int HiredCount);

internal sealed record BuildTemplateVersionSnapshot(
    string? Id,
    string? Version,
    string? ChangeLog,
    DateTimeOffset? PublishedAt,
    string? PackageUrl);
