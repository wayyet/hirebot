using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring.StoreSkills;

/// <summary>
/// 通过 BuildService（ncrew-builder）下载用户关联的技能包，并解压成 <c>skills/&lt;slug&gt;/...</c> 路径布局。
/// 与 <see cref="HireBot.Core.Services.Hiring.TemplatePackages.BuildServiceTemplatePackageProvider"/> 共用同一份
/// HttpClient / Authorization 转发约定。
/// </summary>
internal sealed class StoreSkillPackageDownloader(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    ILogger<StoreSkillPackageDownloader> logger) : IStoreSkillPackageDownloader
{
    private const string BuildServiceClientName = "BuildService";
    private const string DefaultApiPrefix = "/api/store";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyDictionary<string, byte[]>> DownloadSkillsAsync(
        IReadOnlyList<string> skillIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (skillIds.Count == 0)
        {
            return result;
        }

        var client = httpClientFactory.CreateClient(BuildServiceClientName);
        if (client.BaseAddress is null)
        {
            throw new InvalidOperationException("BuildService:BaseUrl is not configured.");
        }

        var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawSkillId in skillIds)
        {
            var skillId = rawSkillId?.Trim();
            if (string.IsNullOrWhiteSpace(skillId))
            {
                continue;
            }

            try
            {
                var detail = await FetchSkillDetailAsync(client, skillId, cancellationToken);
                if (detail is null)
                {
                    logger.LogWarning("Store skill detail not found, skipped. SkillId={SkillId}", skillId);
                    continue;
                }

                var slug = ResolveSkillSlug(detail);
                var versionId = ResolveLatestVersionId(detail);
                if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(versionId))
                {
                    logger.LogWarning(
                        "Store skill missing slug or version id, skipped. SkillId={SkillId}, Slug={Slug}, VersionId={VersionId}",
                        skillId,
                        slug,
                        versionId);
                    continue;
                }

                if (!seenSlugs.Add(slug))
                {
                    logger.LogInformation("Store skill slug already merged, skipped duplicate. SkillId={SkillId}, Slug={Slug}", skillId, slug);
                    continue;
                }

                var packageBytes = await DownloadSkillPackageAsync(client, skillId, versionId, cancellationToken);
                if (packageBytes is null || packageBytes.Length == 0)
                {
                    logger.LogWarning("Store skill package download returned empty content, skipped. SkillId={SkillId}", skillId);
                    continue;
                }

                MergeSkillPackageEntries(result, slug, packageBytes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Store skill download failed, skipped. SkillId={SkillId}", skillId);
            }
        }

        return result;
    }

    private async Task<JsonElement?> FetchSkillDetailAsync(HttpClient client, string skillId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(BuildApiPath($"/skills/{Uri.EscapeDataString(skillId)}"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Store skill detail call failed. SkillId={SkillId}, StatusCode={StatusCode}",
                skillId,
                (int)response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    private async Task<byte[]?> DownloadSkillPackageAsync(
        HttpClient client,
        string skillId,
        string versionId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(BuildApiPath(
            $"/skills/{Uri.EscapeDataString(skillId)}/versions/{Uri.EscapeDataString(versionId)}/download"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Store skill download call failed. SkillId={SkillId}, VersionId={VersionId}, StatusCode={StatusCode}",
                skillId,
                versionId,
                (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static string? ResolveSkillSlug(JsonElement? detail)
    {
        if (detail is null || detail.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // 优先使用 name（技能 slug，如 report-synthesis），回退到 displayName
        var slug = TryGetString(detail.Value, "name");
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = TryGetString(detail.Value, "displayName");
        }

        return string.IsNullOrWhiteSpace(slug)
            ? null
            : SanitizeSlug(slug.Trim());
    }

    private static string? ResolveLatestVersionId(JsonElement? detail)
    {
        if (detail is null || detail.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (detail.Value.TryGetProperty("latestVersion", out var latestVersion) &&
            latestVersion.ValueKind == JsonValueKind.Object)
        {
            var id = TryGetString(latestVersion, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    /// <summary>
    /// 把 skill 包 zip 解压并按 <c>skills/&lt;slug&gt;/...</c> 路径合入结果字典。
    /// 自动剥离顶层包裹目录，过滤隐藏文件与临时文件。
    /// </summary>
    private static void MergeSkillPackageEntries(
        Dictionary<string, byte[]> target,
        string slug,
        byte[] packageBytes)
    {
        using var memoryStream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);

        // 收集 entry 规范路径
        var rawEntries = new List<(string Path, ZipArchiveEntry Entry)>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var normalized = NormalizeEntryPath(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsExcludedEntry(normalized))
            {
                continue;
            }

            rawEntries.Add((normalized, entry));
        }

        if (rawEntries.Count == 0)
        {
            return;
        }

        // 探测公共顶层目录（store skill 包通常以 <slug>/ 作为顶层）
        var commonRoot = DetectCommonTopLevelDirectory(rawEntries.Select(item => item.Path).ToList());

        foreach (var (path, entry) in rawEntries)
        {
            var stripped = !string.IsNullOrEmpty(commonRoot) && path.StartsWith(commonRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? path[(commonRoot.Length + 1)..]
                : path;

            if (string.IsNullOrWhiteSpace(stripped))
            {
                continue;
            }

            var targetPath = $"skills/{slug}/{stripped}";
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            // 用户关联的 store skill 是权威来源，覆盖沙箱生成的同名文件
            target[targetPath] = buffer.ToArray();
        }
    }

    private static string NormalizeEntryPath(string entryFullName)
    {
        var segments = entryFullName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(static segment => segment == "." || segment == ".."))
        {
            return string.Empty;
        }

        return string.Join('/', segments);
    }

    private static bool IsExcludedEntry(string normalizedPath)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.StartsWith('.') || string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var lastSegment = segments.Length > 0 ? segments[^1] : normalizedPath;
        return lastSegment.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.EndsWith(".swp", StringComparison.OrdinalIgnoreCase) ||
               lastSegment.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(lastSegment, "Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectCommonTopLevelDirectory(IReadOnlyList<string> normalizedPaths)
    {
        if (normalizedPaths.Count == 0)
        {
            return null;
        }

        string? candidate = null;
        foreach (var path in normalizedPaths)
        {
            var slashIndex = path.IndexOf('/');
            if (slashIndex <= 0)
            {
                return null;
            }

            var head = path[..slashIndex];
            if (candidate is null)
            {
                candidate = head;
                continue;
            }

            if (!string.Equals(candidate, head, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return candidate;
    }

    /// <summary>
    /// 仅保留字母数字、连字符、下划线、点，其他字符替换为连字符，避免路径注入。
    /// </summary>
    private static string SanitizeSlug(string raw)
    {
        var chars = raw.Select(ch =>
            char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'
                ? ch
                : '-').ToArray();
        var slug = new string(chars).Trim('-', '.');
        return string.IsNullOrWhiteSpace(slug) ? "skill" : slug;
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var requestPath = path.StartsWith('/') ? path : "/" + path;
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
}
