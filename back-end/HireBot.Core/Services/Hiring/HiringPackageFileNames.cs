using System.Text;

namespace HireBot.Core.Services.Hiring;

internal static class HiringPackageFileNames
{
    private const int MaxBaseNameLength = 120;
    private const string DigitalEmployeeSuffix = "数字员工";

    public static string BuildFinalPackageFileName(string? templateName, string hireId)
    {
        var fallbackBaseName = string.IsNullOrWhiteSpace(hireId) ? DigitalEmployeeSuffix : $"{hireId.Trim()}-{DigitalEmployeeSuffix}";
        var baseName = NormalizeFileBaseName(templateName, fallbackBaseName);

        if (!baseName.EndsWith(DigitalEmployeeSuffix, StringComparison.Ordinal))
        {
            baseName = $"{baseName}-{DigitalEmployeeSuffix}";
        }

        return $"{baseName}.zip";
    }

    private static string NormalizeFileBaseName(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new StringBuilder(source.Length);
        var previousWasSeparator = false;

        foreach (var character in source)
        {
            if (char.IsControl(character) || IsInvalidWindowsFileNameCharacter(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character) || character == '_' || character == '-')
            {
                if (builder.Length > 0 && !previousWasSeparator)
                {
                    builder.Append('-');
                    previousWasSeparator = true;
                }

                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
        }

        var normalized = builder
            .ToString()
            .Trim(' ', '-', '.');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = fallback;
        }

        return normalized.Length <= MaxBaseNameLength
            ? normalized
            : normalized[..MaxBaseNameLength].Trim(' ', '-', '.');
    }

    private static bool IsInvalidWindowsFileNameCharacter(char character)
    {
        return character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';
    }
}
