namespace HireBot.Abstraction.Models.Sandbox;

public static class SandboxScopeTypes
{
    /// <summary>雇佣流程沙箱，ScopeKey = hire-{Guid}</summary>
    public const string Hire = "hire";

    /// <summary>个人分身运行时沙箱，ScopeKey = instance:{instanceId}</summary>
    public const string Runtime = "runtime";

    /// <summary>托管评估沙箱，ScopeKey = eval-{role}-{Guid}</summary>
    public const string Managed = "managed";
}
