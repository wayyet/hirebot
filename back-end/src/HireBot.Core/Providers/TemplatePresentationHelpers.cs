namespace HireBot.Core.Providers;

internal static class TemplatePresentationHelpers
{
    /// <summary>
    /// 返回第一个非空字符串，全为空则返回 null。
    /// </summary>
    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }

    /// <summary>
    /// 构建默认图标 URL（占位符逻辑）。
    /// </summary>
    public static string? BuildDefaultIconUrl(string templateId, string? name)
    {
        return null;
    }
}
