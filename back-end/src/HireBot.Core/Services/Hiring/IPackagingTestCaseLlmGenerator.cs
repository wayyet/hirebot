using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 基于雇佣会话 History 转录，调用 LLM 生成 live_evaluator 兼容的 testcase JSON。
/// </summary>
internal interface IPackagingTestCaseLlmGenerator
{
    Task<(bool Success, string Json)> TryGenerateAsync(
        PackagingTestCaseGenerationRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record PackagingTestCaseGenerationRequest(
    string TemplateName,
    IReadOnlyDictionary<string, string?> StructuredData,
    IReadOnlyList<HiringConversationMessageDto> HistoryMessages);
