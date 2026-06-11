using System.Text.RegularExpressions;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 打包意图识别（正则集中维护，避免多处规则漂移）。
/// </summary>
internal static partial class PackagingIntentSupport
{
    [GeneratedRegex(
        @"生成(?:实例|产物|数字员工)?包|(?:开始)?生成数字员工(?:包)?|开始(?:生成)?打包|产物包|template_package|package_workspace|ready_for_packaging|instance_packaging|generate\s+(?:the\s+)?(?:digital\s+employee|instance\s+package)(?:\s+package)?|digital\s+employee\s+package|instance\s+package",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex PackagingIntentRegex();

    internal static bool IsPackagingIntent(string? message)
    {
        return !string.IsNullOrWhiteSpace(message) && PackagingIntentRegex().IsMatch(message);
    }
}
