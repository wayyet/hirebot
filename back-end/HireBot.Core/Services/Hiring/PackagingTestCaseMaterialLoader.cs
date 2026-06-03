using System.Text;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 从 hiring_material_files 读取待办上传资料正文，供打包前 testcase Skill 使用。
/// </summary>
internal static class PackagingTestCaseMaterialLoader
{
    internal const int MaxSingleFileCharacters = 8_192;
    internal const int MaxTotalCharacters = 24_576;

    internal static async Task<IReadOnlyList<PackagingMaterialFileSnapshot>> LoadAsync(
        HireBotDbContext dbContext,
        string hireId,
        string sessionId,
        IReadOnlyList<string> allowedStorageRoots,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            return [];
        }

        if (allowedStorageRoots is null || allowedStorageRoots.Count == 0)
        {
            return [];
        }

        var normalizedHireId = hireId.Trim();
        var normalizedSessionId = sessionId.Trim();
        var query = dbContext.HiringMaterialFiles
            .AsNoTracking()
            .Where(item => item.HireId == normalizedHireId && item.DeletedAtUtc == null);
        if (!string.IsNullOrWhiteSpace(normalizedSessionId))
        {
            query = query.Where(item => item.SessionId == normalizedSessionId);
        }

        var records = await query
            .OrderBy(item => item.RequestedCategoryTitle)
            .ThenBy(item => item.RelativePath)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return [];
        }

        var snapshots = new List<PackagingMaterialFileSnapshot>();
        var totalCharacters = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(record.OriginalFileName);
            if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.StoragePath) ||
                !IsStoragePathAllowed(record.StoragePath, allowedStorageRoots) ||
                !File.Exists(record.StoragePath))
            {
                continue;
            }

            string rawContent;
            try
            {
                rawContent = await File.ReadAllTextAsync(record.StoragePath, Encoding.UTF8, cancellationToken);
            }
            catch (IOException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                continue;
            }

            var content = TruncateContent(rawContent.Trim(), MaxSingleFileCharacters);
            if (totalCharacters + content.Length > MaxTotalCharacters)
            {
                var remaining = MaxTotalCharacters - totalCharacters;
                if (remaining <= 0)
                {
                    break;
                }

                content = TruncateContent(content, remaining);
            }

            snapshots.Add(new PackagingMaterialFileSnapshot(
                record.RelativePath,
                record.OriginalFileName,
                record.RequestedCategoryTitle,
                content));

            totalCharacters += content.Length;
            if (totalCharacters >= MaxTotalCharacters)
            {
                break;
            }
        }

        return snapshots;
    }

    private static bool IsStoragePathAllowed(string storagePath, IReadOnlyList<string> allowedStorageRoots)
    {
        foreach (var root in allowedStorageRoots)
        {
            if (HireBotPathResolver.IsPathUnderRoot(storagePath, root))
            {
                return true;
            }
        }

        return false;
    }

    internal static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength)
        {
            return content;
        }

        return content[..maxLength];
    }
}

internal sealed record PackagingMaterialFileSnapshot(
    string RelativePath,
    string OriginalFileName,
    string? RequestedCategoryTitle,
    string Content);
