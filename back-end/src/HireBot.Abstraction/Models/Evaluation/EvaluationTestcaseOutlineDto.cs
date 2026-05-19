namespace HireBot.Abstraction.Models.Evaluation;

/// <summary>
/// 评估用例大纲，从目标模板的 testcase 文件中解析，供前端展示评估场景列表。
/// </summary>
public sealed record EvaluationTestcaseOutlineDto(
    string TestcaseId,
    string Title,
    string UserRequest);
