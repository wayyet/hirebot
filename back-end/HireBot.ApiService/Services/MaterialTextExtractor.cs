using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace HireBot.ApiService.Services;

/// <summary>
/// PDF / DOCX 文本提取工具，用于将用户上传的二进制资料文件转为纯文本伴生 .md。
/// 提取失败时不抛异常，返回占位提示，确保上传流程不被阻断。
/// </summary>
internal static class MaterialTextExtractor
{
    private const int MaxExtractedChars = 500_000;

    private static readonly HashSet<string> ExtractableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx",
        ".doc"
    };

    /// <summary>
    /// 根据扩展名判断是否需要文本提取。
    /// </summary>
    public static bool RequiresTextExtraction(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && ExtractableExtensions.Contains(extension);

    /// <summary>
    /// 生成伴生 .md 文件名，例如 "报告.pdf" → "报告.pdf.md"。
    /// </summary>
    public static string BuildCompanionMarkdownFileName(string originalFileName)
    {
        var name = string.IsNullOrWhiteSpace(originalFileName) ? "file" : originalFileName.Trim();
        return $"{name}.md";
    }

    /// <summary>
    /// 从流中提取文本，根据扩展名自动选择 PDF 或 DOCX 解析器。
    /// </summary>
    public static string ExtractText(Stream stream, string extension)
    {
        if (stream is null || !stream.CanRead) return "[文件解析失败: 无法读取文件流]";

        var ext = (extension ?? string.Empty).Trim().ToLowerInvariant();

        try
        {
            return ext switch
            {
                ".pdf" => ExtractPdfText(stream),
                ".docx" or ".doc" => ExtractDocxText(stream),
                _ => string.Empty
            };
        }
        catch (Exception ex)
        {
            return $"[文件解析失败: {ex.Message}]";
        }
    }

    /// <summary>
    /// 使用 UglyToad.PdfPig 从 PDF 流中逐页提取纯文本。
    /// </summary>
    internal static string ExtractPdfText(Stream stream)
    {
        var sb = new StringBuilder();

        using var document = PdfDocument.Open(stream);
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(pageText);

                if (sb.Length > MaxExtractedChars)
                {
                    sb.Length = MaxExtractedChars;
                    sb.AppendLine();
                    sb.Append("[文本过长，已截断]");
                    break;
                }
            }
        }

        return sb.Length == 0
            ? "[此文件未包含可提取的文本内容]"
            : sb.ToString();
    }

    /// <summary>
    /// 使用 DocumentFormat.OpenXml 从 DOCX 流中提取纯文本。
    /// 遍历文档主体中的段落、表格单元格和简单列表。
    /// </summary>
    internal static string ExtractDocxText(Stream stream)
    {
        var sb = new StringBuilder();

        using var wordDoc = WordprocessingDocument.Open(stream, false);
        var body = wordDoc.MainDocumentPart?.Document?.Body;
        if (body is null) return "[此文件未包含可提取的文本内容]";

        foreach (var element in body.Elements())
        {
            var localName = element.LocalName;

            if (localName == "p")
            {
                // 段落
                var text = element.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(text.Trim());
                }
            }
            else if (localName == "tbl")
            {
                // 表格：逐行提取单元格文本
                foreach (var row in element.Elements().Where(e => e.LocalName == "tr"))
                {
                    var cells = row.Elements()
                        .Where(e => e.LocalName == "tc")
                        .Select(tc => tc.InnerText.Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList();

                    if (cells.Count > 0)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(string.Join("\t", cells));
                    }
                }
            }
        }

        if (sb.Length > MaxExtractedChars)
        {
            sb.Length = MaxExtractedChars;
            sb.AppendLine();
            sb.Append("[文本过长，已截断]");
        }

        return sb.Length == 0
            ? "[此文件未包含可提取的文本内容]"
            : sb.ToString();
    }
}
