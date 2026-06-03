namespace HireBot.Abstraction.Models.Evaluation;

public sealed record EvaluationQuestionCardDto(
    string TestcaseId,
    string Title,
    string Prompt,
    string ScoringHint,
    IReadOnlyList<string> Steps,
    string SourceFile);
