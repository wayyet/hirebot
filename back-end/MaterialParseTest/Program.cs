using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

var pdfPath = @"C:\Users\zhang\Desktop\资料\化妆品排产员历史排产与插单案例资料.pdf";
var docxPath = @"C:\Users\zhang\Desktop\资料\化妆品排产员历史排产与插单案例资料.docx";

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine("PDF 解析测试");
Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine($"文件: {pdfPath}");
Console.WriteLine($"大小: {new FileInfo(pdfPath).Length / 1024} KB");
Console.WriteLine();

try
{
    var pdfText = ExtractPdfText(pdfPath);
    Console.WriteLine($"提取字符数: {pdfText.Length}");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine(pdfText);
}
catch (Exception ex)
{
    Console.WriteLine($"解析失败: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine("DOCX 解析测试");
Console.WriteLine("=".PadRight(80, '='));
Console.WriteLine($"文件: {docxPath}");
Console.WriteLine($"大小: {new FileInfo(docxPath).Length / 1024} KB");
Console.WriteLine();

try
{
    var docxText = ExtractDocxText(docxPath);
    Console.WriteLine($"提取字符数: {docxText.Length}");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine(docxText);
}
catch (Exception ex)
{
    Console.WriteLine($"解析失败: {ex.Message}");
}

static string ExtractPdfText(string filePath)
{
    var sb = new StringBuilder();
    using var document = PdfDocument.Open(filePath);
    foreach (var page in document.GetPages())
    {
        var pageText = page.Text;
        if (!string.IsNullOrWhiteSpace(pageText))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(pageText);
        }
    }
    return sb.Length == 0 ? "[此文件未包含可提取的文本内容]" : sb.ToString();
}

static string ExtractDocxText(string filePath)
{
    var sb = new StringBuilder();
    using var wordDoc = WordprocessingDocument.Open(filePath, false);
    var body = wordDoc.MainDocumentPart?.Document.Body;
    if (body is null) return "[此文件未包含可提取的文本内容]";

    foreach (var element in body.Elements())
    {
        var localName = element.LocalName;
        if (localName == "p")
        {
            var text = element.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(text.Trim());
            }
        }
        else if (localName == "tbl")
        {
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

    return sb.Length == 0 ? "[此文件未包含可提取的文本内容]" : sb.ToString();
}
