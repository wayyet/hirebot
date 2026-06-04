using System.IO.Compression;
using HireBot.Core.Services.Hiring.TemplatePackages;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 模板包存档构建器（替代旧的 EmployeeHiringService.BuildDigitalEmployeeArchive 静态方法）。
/// </summary>
internal static class TemplatePackageArchiveBuilder
{
    /// <summary>
    /// 将模板包定义构建为 ZIP 存档字节数组。
    /// </summary>
    public static byte[] BuildArchive(TemplatePackageDefinition templatePackage)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in templatePackage.PackageFiles)
            {
                var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
        }
        return memoryStream.ToArray();
    }
}
