namespace HireBot.Abstraction.Models.Hiring;

public static class HiringAuditDecision
{
    public const string Approve = "APPROVE";
    public const string RequestChanges = "REQUEST_CHANGES";
    public const string RollbackToStage = "ROLLBACK_TO_STAGE";
    public const string ForceOverride = "FORCE_OVERRIDE";
}
