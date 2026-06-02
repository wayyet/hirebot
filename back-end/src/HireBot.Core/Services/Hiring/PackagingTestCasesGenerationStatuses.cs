namespace HireBot.Core.Services.Hiring;

internal static class PackagingTestCasesGenerationStatuses
{
    internal const string NotAsked = "not_asked";
    internal const string WaitingConfirm = "waiting_confirm";
    internal const string Generating = "generating";
    internal const string Generated = "generated";
    internal const string Skipped = "skipped";
    internal const string Failed = "failed";

    internal static string Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return NotAsked;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized switch
        {
            WaitingConfirm or Generating or Generated or Skipped or Failed => normalized,
            _ => NotAsked
        };
    }
}
