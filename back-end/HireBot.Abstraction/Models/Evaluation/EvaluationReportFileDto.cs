namespace HireBot.Abstraction.Models.Evaluation;

/// <summary>评估报告文件下载结果（物理文件信息，供控制器流式返回）</summary>
public sealed record EvaluationReportFileDto(
    string PhysicalPath,
    string MimeType,
    string FileName);
