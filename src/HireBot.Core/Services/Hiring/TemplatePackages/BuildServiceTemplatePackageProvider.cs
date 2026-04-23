using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring.TemplatePackages;

internal sealed class BuildServiceTemplatePackageProvider(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<BuildServiceTemplatePackageProvider> logger) : ITemplatePackageProvider
{
    private const string BuildServiceClientName = "BuildService";
    private const string DefaultApiPrefix = "/api/store";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TemplatePackageDefinition> LoadAsync(string templateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("templateId 不能为空");
        }

        var client = httpClientFactory.CreateClient(BuildServiceClientName);
        if (client.BaseAddress is null)
        {
            throw new InvalidOperationException("BuildService:BaseUrl 未配置");
        }

        var normalizedTemplateId = templateId.Trim();
        var detail = await SendForJsonAsync<BuildTemplateDetailResponse>(
            client,
            BuildApiPath($"/templates/{Uri.EscapeDataString(normalizedTemplateId)}"),
            cancellationToken);

        if (!detail.Success || detail.Data is null)
        {
            throw new InvalidOperationException(
                $"模板资产详情读取失败. TemplateId={normalizedTemplateId}, Message={detail.Message}");
        }

        var packageUrl = detail.Data.LatestVersion?.PackageUrl;
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new InvalidOperationException($"模板 {normalizedTemplateId} 当前版本未生成可下载包");
        }

        var packageBytes = await SendForBytesAsync(client, packageUrl.Trim(), cancellationToken);
        if (!packageBytes.Success || packageBytes.Data is null || packageBytes.Data.Length == 0)
        {
            throw new InvalidOperationException(
                $"模板包下载失败. TemplateId={normalizedTemplateId}, Message={packageBytes.Message}");
        }

        return BuildPackageDefinition(normalizedTemplateId, detail.Data, packageBytes.Data);
    }

    private TemplatePackageDefinition BuildPackageDefinition(
        string templateId,
        BuildTemplateDetailResponse detail,
        byte[] packageBytes)
    {
        using var archiveStream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            !entry.FullName.EndsWith("/", StringComparison.Ordinal) &&
            !IsIgnoredEntry(entry.FullName) &&
            string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            throw new InvalidOperationException("模板包中缺少 manifest.json");
        }

        var manifestJson = ReadEntryText(manifestEntry);
        var manifest = JsonSerializer.Deserialize<TemplateManifestDocument>(manifestJson, JsonOptions)
                       ?? throw new InvalidOperationException("模板 manifest 解析失败");
        var entryIndex = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal) && !IsIgnoredEntry(entry.FullName))
            .ToDictionary(entry => NormalizeZipPath(entry.FullName), entry => entry, StringComparer.OrdinalIgnoreCase);

        var manifestDirectory = Path.GetDirectoryName(NormalizeZipPath(manifestEntry.FullName))?.Replace('\\', '/');
        var ontologySlices = new List<TemplateOntologySliceAsset>();
        foreach (var slice in manifest.OntologySlices ?? [])
        {
            if (string.IsNullOrWhiteSpace(slice.Path))
            {
                continue;
            }

            var entry = ResolveEntry(entryIndex, manifestDirectory, slice.Path);
            if (entry is null)
            {
                logger.LogWarning("Template ontology slice missing in package. TemplateId={TemplateId}, Path={Path}", templateId, slice.Path);
                continue;
            }

            var content = ReadEntryText(entry);
            ontologySlices.Add(new TemplateOntologySliceAsset(
                Name: FirstNonEmpty(slice.Name, Path.GetFileNameWithoutExtension(entry.FullName)),
                RelativePath: NormalizeRelativePath(slice.Path),
                Type: FirstNonEmpty(slice.Type, "digital_employee_slice"),
                Required: slice.Required ?? false,
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }

        var requiredSkills = new List<TemplateSkillAsset>();
        foreach (var skill in manifest.Skills ?? [])
        {
            if (skill.Required != true || string.IsNullOrWhiteSpace(skill.Path))
            {
                continue;
            }

            var entry = ResolveEntry(entryIndex, manifestDirectory, skill.Path);
            if (entry is null)
            {
                logger.LogWarning("Template required skill missing in package. TemplateId={TemplateId}, Path={Path}", templateId, skill.Path);
                continue;
            }

            var content = ReadEntryText(entry);
            requiredSkills.Add(new TemplateSkillAsset(
                Name: FirstNonEmpty(skill.Name, Path.GetFileNameWithoutExtension(entry.FullName)),
                RelativePath: NormalizeRelativePath(skill.Path),
                Required: true,
                Content: content,
                ContentHash: HiringAssetFileSystem.ComputeContentHash(content)));
        }

        return new TemplatePackageDefinition(
            RequestedTemplateId: templateId,
            PackageId: FirstNonEmpty(manifest.Name, detail.Name, templateId),
            PackageVersion: FirstNonEmpty(detail.LatestVersion?.Version, manifest.Version, detail.CurrentVersion, "v1-placeholder"),
            PackageHash: Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(packageBytes)),
            PackageRootPath: $"build-service:{templateId}",
            ManifestJson: manifestJson,
            DisplayName: FirstNonEmpty(manifest.DisplayName, detail.Name, templateId),
            Description: FirstNonEmpty(manifest.Description, detail.Description, detail.Positioning, "NCrew template package"),
            OntologySlices: ontologySlices,
            RequiredSkills: requiredSkills);
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
            return model is null
                ? RemoteCallResult<T>.Failure(502, "Build service response deserialized to null.")
                : RemoteCallResult<T>.Ok(model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build service JSON request failed. Path={Path}", path);
            return RemoteCallResult<T>.Failure(502, "Build service request failed.");
        }
    }

    private async Task<RemoteCallResult<byte[]>> SendForBytesAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(path);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                return RemoteCallResult<byte[]>.Failure(
                    (int)response.StatusCode,
                    ExtractRemoteMessage(payload) ?? $"Build service package download failed (HTTP {(int)response.StatusCode}).");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length == 0
                ? RemoteCallResult<byte[]>.Failure(502, "Build service package download returned empty content.")
                : RemoteCallResult<byte[]>.Ok(bytes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Build service package download failed. Path={Path}", path);
            return RemoteCallResult<byte[]>.Failure(502, "Build service package download failed.");
        }
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var requestPath = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? path
            : NormalizeHttpPath(path);
        var request = new HttpRequestMessage(HttpMethod.Get, requestPath);

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

    private string BuildApiPath(string path)
    {
        var prefix = configuration["BuildService:ApiPrefix"];
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? DefaultApiPrefix
            : "/" + prefix.Trim().Trim('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return $"{normalizedPrefix}{normalizedPath}";
    }

    private static string NormalizeHttpPath(string path)
    {
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static bool IsIgnoredEntry(string fullName)
    {
        var normalizedName = fullName.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedName);
        return normalizedName
                   .Split('/', StringSplitOptions.RemoveEmptyEntries)
                   .Any(segment => string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase)) ||
               fileName.StartsWith("._", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static ZipArchiveEntry? ResolveEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entryIndex,
        string? manifestDirectory,
        string relativePath)
    {
        var normalizedRelative = NormalizeRelativePath(relativePath);
        if (!string.IsNullOrWhiteSpace(manifestDirectory))
        {
            var combined = $"{manifestDirectory.TrimEnd('/')}/{normalizedRelative}";
            if (entryIndex.TryGetValue(combined, out var scopedEntry))
            {
                return scopedEntry;
            }
        }

        return entryIndex.TryGetValue(normalizedRelative, out var entry) ? entry : null;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeZipPath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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

    private sealed record TemplateManifestDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("ontology_slices")] IReadOnlyList<TemplateOntologySliceDocument>? OntologySlices,
        [property: JsonPropertyName("skills")] IReadOnlyList<TemplateSkillDocument>? Skills);

    private sealed record TemplateOntologySliceDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("required")] bool? Required);

    private sealed record TemplateSkillDocument(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("required")] bool? Required);

    private sealed record RemoteCallResult<T>(bool Success, int StatusCode, string Message, T? Data)
    {
        public static RemoteCallResult<T> Ok(T data)
        {
            return new RemoteCallResult<T>(true, 200, string.Empty, data);
        }

        public static RemoteCallResult<T> Failure(int statusCode, string message)
        {
            var normalizedStatusCode = statusCode <= 0 ? 502 : statusCode;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "Build service call failed." : message;
            return new RemoteCallResult<T>(false, normalizedStatusCode, normalizedMessage, default);
        }
    }
}
