using System.Text;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 从 WorkingTemplatePackage 提取文本快照（manifest / skills / ontology / config），供打包前 testcase Skill 使用。
/// </summary>
internal static class PackagingTestCaseTemplateSnapshotBuilder
{
    internal const int MaxSingleFileCharacters = 6_144;
    internal const int MaxTotalCharacters = 32_768;

    private static readonly string[] PriorityPrefixes =
    [
        "manifest.json",
        "skills/",
        "ontology/",
        "config/",
        "external/"
    ];

    internal static IReadOnlyList<PackagingTemplateFileSnapshot> Build(
        IReadOnlyList<TemplatePackageFileAsset> packageFiles)
    {
        if (packageFiles.Count == 0)
        {
            return [];
        }

        var candidates = packageFiles
            .Select(file => new ScoredFile(file, GetPriorityScore(file.RelativePath)))
            .Where(item => item.Score >= 0 && IsTextSnapshotCandidate(item.File.RelativePath))
            .OrderBy(item => item.Score)
            .ThenBy(item => item.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var snapshots = new List<PackagingTemplateFileSnapshot>();
        var totalCharacters = 0;

        foreach (var candidate in candidates)
        {
            if (!TryDecodeUtf8Text(candidate.File.Content, out var rawContent) ||
                string.IsNullOrWhiteSpace(rawContent))
            {
                continue;
            }

            var content = PackagingTestCaseMaterialLoader.TruncateContent(rawContent.Trim(), MaxSingleFileCharacters);
            if (totalCharacters + content.Length > MaxTotalCharacters)
            {
                var remaining = MaxTotalCharacters - totalCharacters;
                if (remaining <= 0)
                {
                    break;
                }

                content = PackagingTestCaseMaterialLoader.TruncateContent(content, remaining);
            }

            snapshots.Add(new PackagingTemplateFileSnapshot(
                NormalizePath(candidate.File.RelativePath),
                content));

            totalCharacters += content.Length;
            if (totalCharacters >= MaxTotalCharacters)
            {
                break;
            }
        }

        return snapshots;
    }

    internal static bool IsTextSnapshotCandidate(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        var extension = Path.GetExtension(normalized);

        if (string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (extension is ".md" or ".json" or ".txt")
        {
            return normalized.StartsWith("skills/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("ontology/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("external/", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    internal static int GetPriorityScore(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        if (string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        for (var index = 0; index < PriorityPrefixes.Length; index++)
        {
            var prefix = PriorityPrefixes[index];
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static bool TryDecodeUtf8Text(byte[] content, out string text)
    {
        text = string.Empty;
        if (content.Length == 0)
        {
            return false;
        }

        if (ContainsBinaryNull(content))
        {
            return false;
        }

        try
        {
            text = Encoding.UTF8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool ContainsBinaryNull(byte[] content)
    {
        var scanLength = Math.Min(content.Length, 512);
        for (var index = 0; index < scanLength; index++)
        {
            if (content[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePath(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private sealed record ScoredFile(TemplatePackageFileAsset File, int Score);
}

internal sealed record PackagingTemplateFileSnapshot(
    string RelativePath,
    string Content);
