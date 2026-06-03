using System.Text.Encodings.Web;
using System.Text.Json;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// packaging 测试用例 JSON 的人类可读写出配置（明文中文 + 缩进，与 intermediate 样例一致）。
/// </summary>
internal static class PackagingTestCasesJsonFormatting
{
    internal static readonly JsonWriterOptions HumanReadableWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true
    };

    internal static readonly JsonSerializerOptions HumanReadableSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 将已校验的合并主文件 JSON 重序列化为明文中文 + 缩进（与 intermediate 样例一致）。
    /// </summary>
    internal static string FormatAsHumanReadableJson(JsonElement root) =>
        JsonSerializer.Serialize(root, HumanReadableSerializerOptions);
}
