using System.Security.Cryptography;
using System.Text;

namespace HireBot.Core.Services.Hiring;

internal static class HiringAssetFileSystem
{
    public static string ResolveDirectory(string contentRootPath, string? configuredPath, string defaultRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath.Trim()
                : Path.GetFullPath(configuredPath.Trim(), contentRootPath);
        }

        return Path.GetFullPath(defaultRelativePath, contentRootPath);
    }

    public static bool IsIgnoredDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        return string.Equals(name, "__MACOSX", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIgnoredFile(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.StartsWith("._", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, ".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIgnoredPath(string path)
    {
        return IsIgnoredFile(path) || path
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase));
    }

    public static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        var trimmed = value.Trim();
        var buffer = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var current = trimmed[i];
            buffer[i] = char.IsLetterOrDigit(current) || current is '-' or '_' or '.'
                ? current
                : '_';
        }

        return new string(buffer);
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static async Task<string> ComputeDirectoryHashAsync(string rootPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            return ComputeContentHash(rootPath);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var filePath in Directory
                     .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
                     .Where(file => !IsIgnoredPath(file))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
