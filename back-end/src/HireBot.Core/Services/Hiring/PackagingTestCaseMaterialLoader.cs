using System.Text;
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hireId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return [];
        }

        var records = await dbContext.HiringMaterialFiles
            .AsNoTracking()
            .Where(item =>
                item.HireId == hireId.Trim() &&
                item.SessionId == sessionId.Trim() &&
                item.DeletedAtUtc == null)
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

            if (string.IsNullOrWhiteSpace(record.StoragePath) || !File.Exists(record.StoragePath))
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
