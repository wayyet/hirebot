using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Core.Services.Hiring;

internal sealed class PersistentHiringRuntimeStore(HireBotDbContext dbContext) : IHiringRuntimeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public HiringRuntimeContext? Get(string hireId)
    {
        var entity = dbContext.HiringRuntimeStates
            .AsNoTracking()
            .FirstOrDefault(item => item.HireId == hireId);

        return entity is null ? null : BuildContext(entity);
    }

    public HiringRuntimeContext? GetBySessionId(string sessionId)
    {
        var entity = dbContext.HiringRuntimeStates
            .AsNoTracking()
            .FirstOrDefault(item => item.SessionId == sessionId);

        return entity is null ? null : BuildContext(entity);
    }

    public void Upsert(HiringRuntimeContext context)
    {
        var entity = dbContext.HiringRuntimeStates
            .FirstOrDefault(item => item.HireId == context.HireId);
        var now = DateTimeOffset.UtcNow;

        var metaJson = JsonSerializer.Serialize(PersistedHiringMeta.From(context), JsonOptions);
        var packagesJson = JsonSerializer.Serialize(PersistedHiringPackages.From(context), JsonOptions);
        var workflowStateJson = JsonSerializer.Serialize(PersistedHiringWorkflowState.From(context), JsonOptions);

        if (entity is null)
        {
            dbContext.HiringRuntimeStates.Add(new HiringRuntimeStateEntity
            {
                HireId = context.HireId,
                SessionId = context.SessionId,
                CurrentStage = context.CurrentStage,
                CollectionPhase = context.CollectionPhase,
                PayloadJson = metaJson,
                PackagesJson = packagesJson,
                WorkflowStateJson = workflowStateJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            entity.SessionId = context.SessionId;
            entity.CurrentStage = context.CurrentStage;
            entity.CollectionPhase = context.CollectionPhase;
            entity.PayloadJson = metaJson;
            entity.PackagesJson = packagesJson;
            entity.WorkflowStateJson = workflowStateJson;
            entity.UpdatedAtUtc = now;
        }

        dbContext.SaveChanges();
    }

    private HiringRuntimeContext? BuildContext(HiringRuntimeStateEntity entity)
    {
        var meta = JsonSerializer.Deserialize<PersistedHiringMeta>(entity.PayloadJson, JsonOptions);
        if (meta is null)
            return null;

        // packages 和 workflowState 在旧行（SQL 迁移前）中可能为空对象 {}，运行 migrate-20260516-to-20260517.sql 后才有完整数据。
        var packages = JsonSerializer.Deserialize<PersistedHiringPackages>(entity.PackagesJson, JsonOptions);
        var workflowState = JsonSerializer.Deserialize<PersistedHiringWorkflowState>(entity.WorkflowStateJson, JsonOptions);

        return new HiringRuntimeContext
        {
            HireId = entity.HireId,
            SessionId = entity.SessionId,
            CurrentStage = entity.CurrentStage,
            CollectionPhase = entity.CollectionPhase,
            TemplateId = meta.TemplateId,
            TemplateName = meta.TemplateName,
            OwnerSubject = meta.OwnerSubject,
            TenantId = meta.TenantId,
            OperatorId = meta.OperatorId,
            SandboxId = meta.SandboxId,
            EmployeeId = meta.EmployeeId,
            IsConversationPaused = meta.IsConversationPaused,
            IsConversationResponding = meta.IsConversationResponding,
            IsTemplateUploadPending = meta.IsTemplateUploadPending,
            TemplateUploadRetryCount = meta.TemplateUploadRetryCount,
            TemplateUploadLastError = meta.TemplateUploadLastError,
            TemplateUploadLastAttemptAt = meta.TemplateUploadLastAttemptAt,
            PackagingTestCasesStaged = meta.PackagingTestCasesStaged,
            PackagingTestCasesStatus = string.IsNullOrWhiteSpace(meta.PackagingTestCasesStatus) && meta.PackagingTestCasesStaged
                ? PackagingTestCasesGenerationStatuses.Generated
                : PackagingTestCasesGenerationStatuses.Normalize(meta.PackagingTestCasesStatus),
            PackagingTestCasesLastError = meta.PackagingTestCasesLastError,
            RoleTemplatePackage = packages!.RoleTemplatePackage,
            WorkingTemplatePackage = packages.WorkingTemplatePackage,
            DiscoverySkill = packages.DiscoverySkill,
            StructuredData = workflowState?.StructuredData ?? new Dictionary<string, string?>(),
            Materials = workflowState?.Materials ?? [],
            StageCompletion = workflowState?.StageCompletion ?? [],
            HandoffItems = workflowState?.HandoffItems ?? [],
            LatestDispatches = workflowState?.LatestDispatches ?? [],
            ConfigGovernance = workflowState?.ConfigGovernance,
            ExternalSystemConfig = workflowState?.ExternalSystemConfig,
            StageReadiness = workflowState?.StageReadiness ?? []
        };
    }

    // 身份元数据：TemplateId、OwnerSubject 等标量字段，几乎不变。
    private sealed record PersistedHiringMeta(
        string TemplateId,
        string TemplateName,
        string OwnerSubject,
        string TenantId,
        string OperatorId,
        string SandboxId,
        string? EmployeeId,
        bool IsConversationPaused,
        bool IsConversationResponding,
        bool IsTemplateUploadPending,
        int TemplateUploadRetryCount,
        string? TemplateUploadLastError,
        DateTimeOffset? TemplateUploadLastAttemptAt,
        bool PackagingTestCasesStaged,
        string? PackagingTestCasesStatus,
        string? PackagingTestCasesLastError)
    {
        public static PersistedHiringMeta From(HiringRuntimeContext context) => new(
            context.TemplateId,
            context.TemplateName,
            context.OwnerSubject,
            context.TenantId,
            context.OperatorId,
            context.SandboxId,
            context.EmployeeId,
            context.IsConversationPaused,
            context.IsConversationResponding,
            context.IsTemplateUploadPending,
            context.TemplateUploadRetryCount,
            context.TemplateUploadLastError,
            context.TemplateUploadLastAttemptAt,
            context.PackagingTestCasesStaged,
            PackagingTestCasesGenerationStatuses.Normalize(context.PackagingTestCasesStatus),
            context.PackagingTestCasesLastError);
    }

    // 模板包定义：体积较大（含 PackageFiles 文件内容），独立列便于按需加载。
    private sealed record PersistedHiringPackages(
        TemplatePackageDefinition RoleTemplatePackage,
        TemplatePackageDefinition WorkingTemplatePackage,
        DiscoverySkillDefinition DiscoverySkill)
    {
        public static PersistedHiringPackages From(HiringRuntimeContext context) => new(
            context.RoleTemplatePackage,
            context.WorkingTemplatePackage,
            context.DiscoverySkill);
    }

    // 动态工作流数据：每轮对话都可能更新，独立列减少写入 payload 大小。
    private sealed record PersistedHiringWorkflowState(
        IReadOnlyDictionary<string, string?> StructuredData,
        IReadOnlyList<HiringConversationMaterialDto> Materials,
        IReadOnlyList<HiringStageCompletionDto> StageCompletion,
        IReadOnlyList<HiringWorkflowHandoffDto> HandoffItems,
        IReadOnlyList<HiringDispatchRecordDto> LatestDispatches,
        HiringConfigGovernanceStateDto? ConfigGovernance,
        HiringExternalSystemConfigState? ExternalSystemConfig,
        IReadOnlyList<HiringStageReadinessDto> StageReadiness)
    {
        public static PersistedHiringWorkflowState From(HiringRuntimeContext context) => new(
            context.StructuredData,
            context.Materials,
            context.StageCompletion,
            context.HandoffItems,
            context.LatestDispatches,
            context.ConfigGovernance,
            context.ExternalSystemConfig,
            context.StageReadiness);
    }
}
