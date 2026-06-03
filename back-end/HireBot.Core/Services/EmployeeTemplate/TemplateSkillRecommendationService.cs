using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Core.Services.Hiring.TemplatePackages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeTemplate;

internal sealed class TemplateSkillRecommendationService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    IMemoryCache memoryCache,
    ILogger<TemplateSkillRecommendationService> logger) : ITemplateSkillRecommendationService
{
    private const string BuildServiceClientName = "BuildService";
    private const string DefaultApiPrefix = "/api/store";
    private const int StoreSkillPageSize = 100;
    private const int MaxStoreSkillPages = 20;
    private const int DefaultLimit = 5;
    private const int MaxLimit = 10;
    private const double MinimumRecommendationScore = 8d;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex CategoryRegex = new(
        @"\bcategory\s*[:：]\s*[""']?(?<value>[a-zA-Z0-9_.\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "this", "that", "when", "will", "skill", "skills",
        "use", "uses", "using", "name", "description", "metadata", "version", "license", "source",
        "sources", "template", "templates", "user", "users", "file", "files", "output", "input",
        "当前", "模板", "技能", "能力", "名称", "描述", "用户", "使用", "输出", "输入", "需要",
        "进行", "生成", "处理", "相关", "系统", "一个", "这个", "可以", "支持", "默认"
    };

    private static readonly IReadOnlyDictionary<string, string[]> SynonymGroups =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["document"] =
            [
                "document", "doc", "docx", "word", "report", "proposal", "contract", "manual",
                "文档", "文件", "报告", "方案", "合同", "说明书", "手册", "材料", "资料", "工单", "草稿"
            ],
            ["spreadsheet"] =
            [
                "spreadsheet", "excel", "xlsx", "xls", "csv", "tsv", "table", "tabular",
                "表格", "数据表", "台账", "清单", "报表", "明细", "统计"
            ],
            ["presentation"] =
            [
                "presentation", "powerpoint", "ppt", "pptx", "slide", "slides", "deck",
                "演示", "幻灯片", "路演", "汇报", "展示"
            ],
            ["pdf"] =
            [
                "pdf", "print", "printable", "formal", "layout", "排版", "版式", "打印", "正式"
            ],
            ["data"] =
            [
                "data", "analytics", "analysis", "dashboard", "metric", "kpi", "visualization",
                "数据", "分析", "看板", "指标", "可视化", "图表"
            ],
            ["finance"] =
            [
                "finance", "financial", "budget", "valuation", "model", "forecast",
                "财务", "预算", "估值", "模型", "预测", "资金"
            ],
            ["customer"] =
            [
                "customer", "client", "service", "support", "sales", "crm",
                "客户", "客服", "售前", "销售", "线索", "咨询"
            ],
            ["workflow"] =
            [
                "workflow", "process", "handoff", "orchestration", "automation",
                "流程", "编排", "交接", "自动化", "任务"
            ]
        };

    private static readonly IReadOnlyDictionary<string, string> SynonymLookup = BuildSynonymLookup();

    public async Task<ApiResponse<IReadOnlyList<RecommendedSkillDto>>> GetRecommendedSkillsAsync(
        string templateId,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<IReadOnlyList<RecommendedSkillDto>>.ErrorResponse(400, "templateId 不能为空");
        }

        var normalizedTemplateId = templateId.Trim();
        var normalizedLimit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        var stopwatch = Stopwatch.StartNew();

        EmployeeTemplateDefinition? template;
        try
        {
            template = await templateDataProvider.GetByIdAsync(normalizedTemplateId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Recommended skills template lookup failed. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<IReadOnlyList<RecommendedSkillDto>>.ErrorResponse(502, ex.Message);
        }

        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<IReadOnlyList<RecommendedSkillDto>>.ErrorResponse(404, "模板不存在或已下架");
        }

        var excludedSkillNames = await LoadTemplateSkillNamesAsync(normalizedTemplateId, template, cancellationToken);

        IReadOnlyList<StoreSkillCandidate> candidates;
        var cacheHit = false;
        try
        {
            var cacheKey = BuildSkillCacheKey();
            if (memoryCache.TryGetValue(cacheKey, out IReadOnlyList<StoreSkillCandidate>? cached) && cached is not null)
            {
                cacheHit = true;
                candidates = cached;
            }
            else
            {
                candidates = await FetchAllStoreSkillsAsync(cancellationToken);
                memoryCache.Set(cacheKey, candidates, TimeSpan.FromMinutes(2));
            }
        }
        catch (BuildServiceRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Recommended skills upstream unavailable. TemplateId={TemplateId}, StatusCode={StatusCode}",
                normalizedTemplateId,
                ex.StatusCode);
            return ApiResponse<IReadOnlyList<RecommendedSkillDto>>.ErrorResponse(ex.StatusCode, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Recommended skills request rejected. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<IReadOnlyList<RecommendedSkillDto>>.ErrorResponse(401, ex.Message);
        }

        var recommendations = RankSkills(template, candidates, excludedSkillNames, normalizedLimit);
        stopwatch.Stop();
        logger.LogInformation(
            "Recommended skills ranked. TemplateId={TemplateId}, CandidateCount={CandidateCount}, MatchCount={MatchCount}, CacheHit={CacheHit}, ElapsedMs={ElapsedMs}",
            normalizedTemplateId,
            candidates.Count,
            recommendations.Count,
            cacheHit,
            stopwatch.ElapsedMilliseconds);

        return ApiResponse<IReadOnlyList<RecommendedSkillDto>>.SuccessResponse(recommendations, "推荐技能加载成功");
    }

    private async Task<IReadOnlySet<string>> LoadTemplateSkillNamesAsync(
        string templateId,
        EmployeeTemplateDefinition template,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prerequisite in template.Prerequisites)
        {
            if (prerequisite.Purpose.Contains("skill", StringComparison.OrdinalIgnoreCase))
            {
                AddNormalizedName(names, prerequisite.SystemName);
            }
        }

        try
        {
            var package = await templatePackageProvider.LoadAsync(templateId, cancellationToken);
            foreach (var skill in package.Skills)
            {
                AddNormalizedName(names, skill.Name);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Template package skill names unavailable for recommendation exclusion. TemplateId={TemplateId}", templateId);
        }

        return names;
    }

    private IReadOnlyList<RecommendedSkillDto> RankSkills(
        EmployeeTemplateDefinition template,
        IReadOnlyList<StoreSkillCandidate> candidates,
        IReadOnlySet<string> excludedSkillNames,
        int limit)
    {
        var templateProfile = BuildTemplateProfile(template);
        var ranked = new List<RankedSkill>(Math.Min(candidates.Count, limit * 4));

        foreach (var candidate in candidates)
        {
            if (!candidate.CanDownload || !candidate.HasPackage || IsUnavailableStatus(candidate.Status))
            {
                continue;
            }

            if (IsTemplateBuiltInSkill(candidate, excludedSkillNames))
            {
                continue;
            }

            var skillProfile = BuildSkillProfile(candidate);
            var score = Score(templateProfile, skillProfile, candidate, out var matchedKeywords);
            if (score < MinimumRecommendationScore || matchedKeywords.Count == 0)
            {
                continue;
            }

            ranked.Add(new RankedSkill(candidate, score, matchedKeywords));
        }

        return ranked
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Candidate.UsageCount)
            .ThenByDescending(item => item.Candidate.UpdatedAt ?? DateTimeOffset.MinValue)
            .Take(limit)
            .Select(item => ToDto(item))
            .ToArray();
    }

    private static RecommendedSkillDto ToDto(RankedSkill ranked)
    {
        var candidate = ranked.Candidate;
        return new RecommendedSkillDto(
            candidate.Id,
            candidate.Name,
            candidate.DisplayName,
            candidate.Description,
            candidate.CurrentVersion,
            candidate.Tags,
            Math.Round((decimal)ranked.Score, 2),
            ranked.MatchedKeywords,
            BuildReason(ranked.MatchedKeywords),
            candidate.CanDownload);
    }

    private static string BuildReason(IReadOnlyList<string> matchedKeywords)
    {
        if (matchedKeywords.Count == 0)
        {
            return "与当前模板能力描述存在弱匹配";
        }

        var display = string.Join("、", matchedKeywords.Take(5));
        return $"命中模板能力关键词：{display}";
    }

    private static double Score(
        TextProfile templateProfile,
        TextProfile skillProfile,
        StoreSkillCandidate candidate,
        out IReadOnlyList<string> matchedKeywords)
    {
        var contributions = new List<(string Token, double Value)>();
        var score = 0d;

        foreach (var (token, skillWeight) in skillProfile.Tokens)
        {
            if (!templateProfile.Tokens.TryGetValue(token, out var templateWeight))
            {
                continue;
            }

            var contribution = Math.Sqrt(templateWeight * skillWeight);
            if (skillProfile.TagTokens.Contains(token))
            {
                contribution += 12d;
            }

            if (skillProfile.CategoryTokens.Contains(token))
            {
                contribution += 10d;
            }

            if (templateProfile.PhraseTokens.Contains(token) || skillProfile.PhraseTokens.Contains(token))
            {
                contribution += 5d;
            }

            score += contribution;
            contributions.Add((token, contribution));
        }

        if (candidate.UsageCount > 0)
        {
            score += Math.Min(3d, Math.Log10(candidate.UsageCount + 1));
        }

        if (candidate.UpdatedAt is { } updatedAt)
        {
            var days = (DateTimeOffset.UtcNow - updatedAt.ToUniversalTime()).TotalDays;
            score += days <= 30 ? 1d : days <= 90 ? 0.5d : 0d;
        }

        matchedKeywords = contributions
            .OrderByDescending(item => item.Value)
            .Select(item => item.Token)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return score;
    }

    private static TextProfile BuildTemplateProfile(EmployeeTemplateDefinition template)
    {
        var sections = new List<(string Text, double Weight)>
        {
            (template.Name, 8d),
            (template.Tagline, 6d),
            (template.Description, 4d),
            (template.DetailDoc, 3d)
        };

        sections.AddRange(template.CoreAbilityTags.Select(value => (value, 7d)));
        sections.AddRange(template.CoreAbilities.Select(value => (value, 7d)));
        sections.AddRange(template.InScope.Select(value => (value, 5d)));
        sections.AddRange(template.SuccessCases.Select(value => (value, 3d)));
        sections.AddRange(template.Prerequisites.Select(value => ($"{value.SystemName} {value.PermissionName} {value.Purpose}", 5d)));

        return BuildTextProfile(sections);
    }

    private static TextProfile BuildSkillProfile(StoreSkillCandidate skill)
    {
        var sections = new List<(string Text, double Weight)>
        {
            (skill.Name, 7d),
            (skill.DisplayName, 7d),
            (skill.Description, 4d),
            (skill.CurrentVersion, 1d)
        };
        sections.AddRange(skill.Tags.Select(value => (value, 8d)));

        var profile = BuildTextProfile(sections);
        foreach (var tag in skill.Tags)
        {
            foreach (var token in Tokenize(tag))
            {
                profile.TagTokens.Add(token);
            }
        }

        var categoryMatch = CategoryRegex.Match(skill.Description);
        if (categoryMatch.Success)
        {
            foreach (var token in Tokenize(categoryMatch.Groups["value"].Value))
            {
                AddWeightedToken(profile.Tokens, token, 9d);
                profile.CategoryTokens.Add(token);
            }
        }

        foreach (var token in Tokenize(skill.Description).Where(token => token is "trigger" or "triggers"))
        {
            profile.PhraseTokens.Add(token);
        }

        return profile;
    }

    private static TextProfile BuildTextProfile(IEnumerable<(string Text, double Weight)> sections)
    {
        var profile = new TextProfile();
        foreach (var (text, weight) in sections)
        {
            foreach (var token in Tokenize(text))
            {
                AddWeightedToken(profile.Tokens, token, weight);
                if (IsPhraseLikeToken(token))
                {
                    profile.PhraseTokens.Add(token);
                }

                if (SynonymLookup.TryGetValue(token, out var canonical))
                {
                    AddWeightedToken(profile.Tokens, canonical, weight * 0.8d);
                    profile.PhraseTokens.Add(canonical);
                }
            }
        }

        return profile;
    }

    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var normalized = text.Trim().ToLowerInvariant();
        var latinBuilder = new StringBuilder();
        var cjkBuilder = new StringBuilder();

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (IsCjk(ch))
                {
                    foreach (var token in FlushLatin(latinBuilder))
                    {
                        yield return token;
                    }

                    cjkBuilder.Append(ch);
                }
                else
                {
                    foreach (var token in FlushCjk(cjkBuilder))
                    {
                        yield return token;
                    }

                    latinBuilder.Append(ch);
                }

                continue;
            }

            foreach (var token in FlushLatin(latinBuilder))
            {
                yield return token;
            }

            foreach (var token in FlushCjk(cjkBuilder))
            {
                yield return token;
            }
        }

        foreach (var token in FlushLatin(latinBuilder))
        {
            yield return token;
        }

        foreach (var token in FlushCjk(cjkBuilder))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> FlushLatin(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            yield break;
        }

        var token = builder.ToString();
        builder.Clear();
        if (token.Length >= 2 && !StopWords.Contains(token))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> FlushCjk(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            yield break;
        }

        var value = builder.ToString();
        builder.Clear();
        if (value.Length == 1)
        {
            yield break;
        }

        if (value.Length <= 8 && !StopWords.Contains(value))
        {
            yield return value;
        }

        foreach (var token in BuildCjkNgrams(value, 2))
        {
            yield return token;
        }

        foreach (var token in BuildCjkNgrams(value, 3))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> BuildCjkNgrams(string value, int size)
    {
        if (value.Length < size)
        {
            yield break;
        }

        for (var i = 0; i <= value.Length - size; i++)
        {
            var token = value.Substring(i, size);
            if (!StopWords.Contains(token))
            {
                yield return token;
            }
        }
    }

    private async Task<IReadOnlyList<StoreSkillCandidate>> FetchAllStoreSkillsAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(BuildServiceClientName);
        if (client.BaseAddress is null)
        {
            throw new InvalidOperationException("BuildService:BaseUrl is not configured.");
        }

        var result = new List<StoreSkillCandidate>();
        for (var page = 1; page <= MaxStoreSkillPages; page++)
        {
            var pageResult = await FetchStoreSkillPageAsync(client, page, cancellationToken);
            result.AddRange(pageResult.Items);

            var fetchedCount = page * StoreSkillPageSize;
            if (pageResult.Items.Count == 0 ||
                pageResult.Items.Count < StoreSkillPageSize ||
                (pageResult.Total > 0 && fetchedCount >= pageResult.Total))
            {
                break;
            }
        }

        return result;
    }

    private async Task<StoreSkillPage> FetchStoreSkillPageAsync(
        HttpClient client,
        int page,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(BuildApiPath($"/skills?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={StoreSkillPageSize.ToString(CultureInfo.InvariantCulture)}"));
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractRemoteMessage(payload) ?? $"Build service skill list call failed (HTTP {(int)response.StatusCode}).";
            throw new BuildServiceRequestException((int)response.StatusCode, message);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new BuildServiceRequestException(502, "Build service returned an empty skill list body.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return ParseSkillPage(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new BuildServiceRequestException(502, "Build service skill list response is invalid JSON.", ex);
        }
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path.StartsWith('/') ? path : "/" + path);
        var authorization = ResolveAuthorizationHeader();
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private string ResolveAuthorizationHeader()
    {
        var incomingAuthorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(incomingAuthorization))
        {
            return incomingAuthorization;
        }

        var staticToken = configuration["BuildService:BearerToken"];
        if (!string.IsNullOrWhiteSpace(staticToken))
        {
            return $"Bearer {staticToken.Trim()}";
        }

        throw new InvalidOperationException(
            "Missing authorization token for build service. Forward client Authorization header or configure BuildService:BearerToken.");
    }

    private string BuildSkillCacheKey()
    {
        var client = httpClientFactory.CreateClient(BuildServiceClientName);
        var baseAddress = client.BaseAddress?.ToString() ?? string.Empty;
        var apiPrefix = ResolveApiPrefix();
        var authorizationHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ResolveAuthorizationHeader())));
        return $"template-skill-recommendations:skills:{baseAddress}:{apiPrefix}:{authorizationHash}";
    }

    private string BuildApiPath(string path)
    {
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return $"{ResolveApiPrefix()}{normalizedPath}";
    }

    private string ResolveApiPrefix()
    {
        var prefix = configuration["BuildService:ApiPrefix"];
        return string.IsNullOrWhiteSpace(prefix)
            ? DefaultApiPrefix
            : "/" + prefix.Trim().Trim('/');
    }

    private static StoreSkillPage ParseSkillPage(JsonElement payload)
    {
        var root = UnwrapEnvelope(payload);
        var itemsElement = FindProperty(root, "items", "records", "list", "skills", "rows");
        if (itemsElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null && root.ValueKind == JsonValueKind.Array)
        {
            itemsElement = root;
        }

        var items = EnumerateArray(itemsElement)
            .Select(ParseSkill)
            .Where(item => item is not null)
            .Cast<StoreSkillCandidate>()
            .ToArray();
        var total = GetInt(root, "total", "totalCount", "count", "totalRecords") ?? items.Length;
        return new StoreSkillPage(total, items);
    }

    private static StoreSkillCandidate? ParseSkill(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = FirstNonEmpty(GetString(element, "id", "skillId", "skill_id"));
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var latestVersion = FindProperty(element, "latestVersion", "latest_version", "currentVersionInfo");
        var name = FirstNonEmpty(GetString(element, "name"), id);
        var displayName = FirstNonEmpty(GetString(element, "displayName", "display_name"), name);
        var currentVersion = FirstNonEmpty(
            GetString(element, "currentVersion", "current_version"),
            GetString(latestVersion, "version"));

        return new StoreSkillCandidate(
            Id: id,
            Name: name,
            DisplayName: displayName,
            Description: FirstNonEmpty(GetString(element, "description", "desc")),
            CurrentVersion: currentVersion,
            Status: FirstNonEmpty(GetString(element, "status")),
            UsageCount: Math.Max(0, GetInt(element, "usageCount", "usage_count", "downloadCount", "download_count") ?? 0),
            UpdatedAt: GetDateTimeOffset(element, "updatedAt", "updated_at", "publishedAt", "published_at"),
            Tags: ExtractStringValues(FindProperty(element, "tags")),
            HasPackage: GetBool(latestVersion, "hasPackage", "has_package"),
            CanDownload: GetBool(latestVersion, "canDownload", "can_download"),
            LatestVersionEntryPoint: FirstNonEmpty(GetString(latestVersion, "entryPoint", "entry_point")));
    }

    private static bool IsTemplateBuiltInSkill(StoreSkillCandidate candidate, IReadOnlySet<string> excludedSkillNames)
    {
        return excludedSkillNames.Contains(NormalizeName(candidate.Name)) ||
               excludedSkillNames.Contains(NormalizeName(candidate.DisplayName));
    }

    private static void AddNormalizedName(HashSet<string> names, string? value)
    {
        var normalized = NormalizeName(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            names.Add(normalized);
        }
    }

    private static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace('_', '-');
    }

    private static bool IsUnavailableStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Equals("deprecated", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("unpublished", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("draft", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("archived", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddWeightedToken(Dictionary<string, double> tokens, string token, double weight)
    {
        if (string.IsNullOrWhiteSpace(token) || StopWords.Contains(token))
        {
            return;
        }

        tokens[token] = tokens.TryGetValue(token, out var current)
            ? current + weight
            : weight;
    }

    private static bool IsPhraseLikeToken(string token)
    {
        return token.Length >= 4 && (token.Contains("doc", StringComparison.OrdinalIgnoreCase) ||
                                    token.Contains("xls", StringComparison.OrdinalIgnoreCase) ||
                                    token.Contains("ppt", StringComparison.OrdinalIgnoreCase) ||
                                    token.Contains("pdf", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCjk(char ch)
    {
        return ch is >= '\u4e00' and <= '\u9fff';
    }

    private static IReadOnlyDictionary<string, string> BuildSynonymLookup()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, values) in SynonymGroups)
        {
            result[canonical] = canonical;
            foreach (var value in values)
            {
                result[value] = canonical;
            }
        }

        return result;
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

    private static IReadOnlyList<JsonElement> EnumerateArray(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().ToArray()
            : [];
    }

    private static IReadOnlyList<string> ExtractStringValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

            return GetString(document.RootElement, "message") ??
                   GetString(document.RootElement, "error");
        }
        catch
        {
            return null;
        }
    }

    private sealed class TextProfile
    {
        public Dictionary<string, double> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TagTokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CategoryTokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PhraseTokens { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StoreSkillPage(int Total, IReadOnlyList<StoreSkillCandidate> Items);

    private sealed record StoreSkillCandidate(
        string Id,
        string Name,
        string DisplayName,
        string Description,
        string CurrentVersion,
        string Status,
        int UsageCount,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<string> Tags,
        bool HasPackage,
        bool CanDownload,
        string LatestVersionEntryPoint);

    private sealed record RankedSkill(
        StoreSkillCandidate Candidate,
        double Score,
        IReadOnlyList<string> MatchedKeywords);

    private sealed class BuildServiceRequestException : Exception
    {
        public BuildServiceRequestException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode <= 0 ? 502 : statusCode;
        }

        public BuildServiceRequestException(int statusCode, string message, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode <= 0 ? 502 : statusCode;
        }

        public int StatusCode { get; }
    }
}
