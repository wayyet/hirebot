namespace HireBot.Abstraction.Models.Evaluation;

/// <summary>评估报告文件下载结果（包含文件流，兼容本地文件系统与云存储）</summary>
public sealed record EvaluationReportFileDto(
    Stream FileStream,
    string MimeType,
    string FileName);
