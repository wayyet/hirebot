using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationSandboxConversationStateDto(
    string EmployeeId,
    string EvalPhase,
    string TargetHireId,
    string TargetRuntimeId,
    string TargetSandboxId,
    string EvaluatorHireId,
    string EvaluatorRuntimeId,
    string EvaluatorSandboxId,
    string SessionId,
    DateTimeOffset? SkillLoadedAtUtc,
    IReadOnlyList<HiringConversationMessageDto> Messages,
    IReadOnlyList<EvaluationQuestionCardDto>? QuestionCards = null);

public sealed record EvaluationSandboxMessageRequestDto
{
    [Required]
    public string Content { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string>? StructuredAnswers { get; init; }

    public IReadOnlyList<HiringConversationMaterialDto>? Materials { get; init; }

    public string? SkillRootPath { get; init; }
}
