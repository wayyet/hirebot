using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HireBot.Core.Services.Hiring;
using Xunit.Sdk;

namespace HireBot.Core.Tests;

/// <summary>
/// 对 final 实包 ZIP 验收 packaging-test-cases 五件套 JSON（复用 <see cref="PackagingTestCasesJsonValidator"/>）。
/// </summary>
internal static class FinalPackageTestCasesZipVerifier
{
    internal const string MergedPath = "testcases/evaluation-test-cases.json";
    internal const string SourcesIndexPath = "ontology/hiring-session/testcases-sources-index.json";
    internal const string HistoryDerivedPath = "ontology/hiring-session/testcases-sources/history-derived.json";
    internal const string MaterialsDerivedPath = "ontology/hiring-session/testcases-sources/materials-derived.json";
    internal const string TemplateDerivedPath = "ontology/hiring-session/testcases-sources/template-derived.json";

    private static readonly string[] RequiredEntryPaths =
    [
        MergedPath,
        SourcesIndexPath,
        HistoryDerivedPath,
        MaterialsDerivedPath,
        TemplateDerivedPath
    ];

    internal static void AssertAcceptance(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new XunitException("ZIP 路径为空。");
        }

        var fullPath = Path.GetFullPath(zipPath);
        if (!File.Exists(fullPath))
        {
            throw new XunitException($"ZIP 不存在: {fullPath}");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length < 1024)
        {
            throw new XunitException($"ZIP 体积过小（{fileInfo.Length} 字节），可能为 API 错误响应: {fullPath}");
        }

        if (!LooksLikeZipFile(fullPath))
        {
            throw new XunitException($"文件不是有效 ZIP: {fullPath}");
        }

        using var archive = ZipFile.OpenRead(fullPath);
        var entries = archive.Entries
            .Select(entry => NormalizeEntryPath(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredPath in RequiredEntryPaths)
        {
            if (!entries.Contains(requiredPath))
            {
                throw new XunitException($"MISSING in FINAL: {requiredPath}");
            }
        }

        AssertMergedJson(ReadZipEntryUtf8(archive, MergedPath));
        AssertSourcesIndexJson(ReadZipEntryUtf8(archive, SourcesIndexPath));
        AssertDerivedJson(ReadZipEntryUtf8(archive, HistoryDerivedPath), "history-derived");
        AssertDerivedJson(ReadZipEntryUtf8(archive, MaterialsDerivedPath), "materials-derived");
        AssertDerivedJson(ReadZipEntryUtf8(archive, TemplateDerivedPath), "template-derived");
    }

    internal static void AssertArtifactStorePackages(string sessionId, string? artifactStoreRoot = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new XunitException("sessionId 为空。");
        }

        var storeRoot = artifactStoreRoot ?? ResolveArtifactStoreRoot();
        var packagesRoot = Path.Combine(storeRoot, "sessions", sessionId.Trim(), "packages");

        foreach (var kind in new[] { "intermediate", "final" })
        {
            var packageZip = Path.Combine(packagesRoot, kind, "package.zip");
            if (!File.Exists(packageZip))
            {
                throw new XunitException($"artifact-store 缺少 {kind} 包: {packageZip}");
            }

            using var archive = ZipFile.OpenRead(packageZip);
            var hasTestcases = archive.Entries.Any(entry =>
                NormalizeEntryPath(entry.FullName)
                    .Contains("testcases/evaluation-test-cases.json", StringComparison.OrdinalIgnoreCase));

            if (!hasTestcases)
            {
                throw new XunitException(
                    $"artifact-store {kind} 包未包含 testcases/evaluation-test-cases.json: {packageZip}");
            }
        }
    }

    private static void AssertMergedJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var source = root.TryGetProperty("source", out var sourceElement) &&
                     sourceElement.ValueKind == JsonValueKind.String
            ? sourceElement.GetString()?.Trim() ?? string.Empty
            : string.Empty;

        if (string.Equals(source, "packaging-fallback", StringComparison.OrdinalIgnoreCase))
        {
            throw new XunitException(
                $"主文件为降级 packaging-fallback，实包验收不通过: {MergedPath}");
        }

        if (!string.Equals(source, "packaging-merged", StringComparison.OrdinalIgnoreCase))
        {
            throw new XunitException(
                $"主文件 source 应为 packaging-merged，实际: '{source}' ({MergedPath})");
        }

        if (!PackagingTestCasesJsonValidator.TryValidateTestCasesJson(json, out _))
        {
            throw new XunitException($"主文件 JSON 结构无效: {MergedPath}");
        }
    }

    private static void AssertSourcesIndexJson(string json)
    {
        if (!PackagingTestCasesJsonValidator.TryValidateSourcesIndexJson(json, out _))
        {
            throw new XunitException($"index JSON 结构无效: {SourcesIndexPath}");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!PackagingTestCasesJsonValidator.TryGetSourcesIndexMaterialFileCount(root, out var materialFiles) ||
            materialFiles < 1)
        {
            throw new XunitException(
                $"index 未记录上传资料（material_files / materials-derived.count 应 ≥ 1）: {SourcesIndexPath}");
        }
    }

    private static void AssertDerivedJson(string json, string expectedSource)
    {
        if (!PackagingTestCasesJsonValidator.TryValidateDerivedTestCasesJson(json, out _))
        {
            throw new XunitException($"derived JSON 结构无效，期望 source={expectedSource}");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("source", out var sourceElement) ||
            sourceElement.ValueKind != JsonValueKind.String ||
            !string.Equals(sourceElement.GetString(), expectedSource, StringComparison.OrdinalIgnoreCase))
        {
            var actual = sourceElement.ValueKind == JsonValueKind.String
                ? sourceElement.GetString()
                : "(missing)";
            throw new XunitException(
                $"derived source 应为 {expectedSource}，实际: '{actual}'");
        }
    }

    private static string ReadZipEntryUtf8(ZipArchive archive, string normalizedPath)
    {
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(NormalizeEntryPath(candidate.FullName), normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new XunitException($"ZIP 内找不到条目: {normalizedPath}");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static bool LooksLikeZipFile(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.Read(header) >= 2 && header[0] == 0x50 && header[1] == 0x4B;
    }

    private static string NormalizeEntryPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string ResolveArtifactStoreRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("HIREBOT_ARTIFACT_STORE_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "back-end",
                "src",
                "HireBot.ApiService",
                "ncrew-hire-data",
                "artifact-store");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new XunitException(
            "无法定位 artifact-store 目录，请设置环境变量 HIREBOT_ARTIFACT_STORE_ROOT。");
    }
}
