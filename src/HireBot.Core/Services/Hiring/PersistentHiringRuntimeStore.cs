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

    private readonly Dictionary<string, HiringRuntimeContext?> cache = new(StringComparer.OrdinalIgnoreCase);

    public HiringRuntimeContext? Get(string hireId)
    {
        if (cache.TryGetValue(hireId, out var cachedContext))
        {
            return cachedContext;
        }

        var entity = dbContext.HiringRuntimeStates
            .AsNoTracking()
            .FirstOrDefault(item => item.HireId == hireId);
        if (entity is null)
        {
            cache[hireId] = null;
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<PersistedHiringRuntimeState>(entity.PayloadJson, JsonOptions);
        if (snapshot is null)
        {
            cache[hireId] = null;
            return null;
        }

        var context = snapshot.ToRuntimeContext();
        cache[hireId] = context;
        return context;
    }

    public void Upsert(HiringRuntimeContext context)
    {
        var entity = dbContext.HiringRuntimeStates
            .FirstOrDefault(item => item.HireId == context.HireId);
        var now = DateTimeOffset.UtcNow;
        var payloadJson = JsonSerializer.Serialize(PersistedHiringRuntimeState.FromRuntimeContext(context), JsonOptions);

        if (entity is null)
        {
            dbContext.HiringRuntimeStates.Add(new HiringRuntimeStateEntity
            {
                SessionId = context.SessionId,
                HireId = context.HireId,
                CurrentStage = context.CurrentStage,
                CollectionPhase = context.CollectionPhase,
                PayloadJson = payloadJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else
        {
            entity.SessionId = context.SessionId;
            entity.CurrentStage = context.CurrentStage;
            entity.CollectionPhase = context.CollectionPhase;
            entity.PayloadJson = payloadJson;
            entity.UpdatedAtUtc = now;
        }

        dbContext.SaveChanges();
        cache[context.HireId] = context;
    }

    private sealed record PersistedHiringRuntimeState(
        string HireId,
        string TemplateId,
        string TemplateName,
        string OwnerSubject,
        string TenantId,
        string OperatorId,
        string SandboxId,
        string SessionId,
        string CurrentStage,
        string CollectionPhase,
        bool IsConversationPaused,
        bool IsConversationResponding,
        string? EmployeeId,
        TemplatePackageDefinition ReferenceTemplatePackage,
        TemplatePackageDefinition RoleTemplatePackage,
        TemplatePackageDefinition WorkingTemplatePackage,
        DiscoverySkillDefinition DiscoverySkill,
        IReadOnlyDictionary<string, string?> StructuredData,
        IReadOnlyList<HiringConversationMaterialDto> Materials,
        IReadOnlyList<HiringConversationMessageDto> Messages,
        IReadOnlyList<HiringAuditLogDto> AuditLogs,
        IReadOnlyList<HiringStageCompletionDto> StageCompletion,
        IReadOnlyList<HiringWorkflowTodoDto> WorkflowTodos,
        IReadOnlyList<HiringDispatchRecordDto> LatestDispatches,
        HiringDiagnosticReportDto? LatestDiagnosticReport,
        IReadOnlyList<HiringCredentialSlotDto> CredentialSlots,
        HiringConfigGovernanceStateDto? ConfigGovernance,
        IReadOnlyList<HiringStageReadinessDto> StageReadiness,
        bool IsTemplateUploadPending,
        int TemplateUploadRetryCount,
        string? TemplateUploadLastError,
        DateTimeOffset? TemplateUploadLastAttemptAt,
        IReadOnlyDictionary<string, byte[]> ArtifactFiles,
        byte[]? ArtifactArchive,
        string? ArtifactArchiveFileName)
    {
        public static PersistedHiringRuntimeState FromRuntimeContext(HiringRuntimeContext context)
        {
            return new PersistedHiringRuntimeState(
                context.HireId,
                context.TemplateId,
                context.TemplateName,
                context.OwnerSubject,
                context.TenantId,
                context.OperatorId,
                context.SandboxId,
                context.SessionId,
                context.CurrentStage,
                context.CollectionPhase,
                context.IsConversationPaused,
                context.IsConversationResponding,
                context.EmployeeId,
                context.ReferenceTemplatePackage,
                context.RoleTemplatePackage,
                context.WorkingTemplatePackage,
                context.DiscoverySkill,
                context.StructuredData,
                context.Materials,
                context.Messages,
                context.AuditLogs,
                context.StageCompletion,
                context.WorkflowTodos,
                context.LatestDispatches,
                context.LatestDiagnosticReport,
                context.CredentialSlots,
                context.ConfigGovernance,
                context.StageReadiness,
                context.IsTemplateUploadPending,
                context.TemplateUploadRetryCount,
                context.TemplateUploadLastError,
                context.TemplateUploadLastAttemptAt,
                context.ArtifactFiles,
                context.ArtifactArchive,
                context.ArtifactArchiveFileName);
        }

        public HiringRuntimeContext ToRuntimeContext()
        {
            return new HiringRuntimeContext
            {
                HireId = HireId,
                TemplateId = TemplateId,
                TemplateName = TemplateName,
                OwnerSubject = OwnerSubject,
                TenantId = TenantId,
                OperatorId = OperatorId,
                SandboxId = SandboxId,
                SessionId = SessionId,
                CurrentStage = CurrentStage,
                CollectionPhase = CollectionPhase,
                IsConversationPaused = IsConversationPaused,
                IsConversationResponding = IsConversationResponding,
                EmployeeId = EmployeeId,
                ReferenceTemplatePackage = ReferenceTemplatePackage,
                RoleTemplatePackage = RoleTemplatePackage,
                WorkingTemplatePackage = WorkingTemplatePackage,
                DiscoverySkill = DiscoverySkill,
                StructuredData = StructuredData,
                Materials = Materials,
                Messages = Messages,
                AuditLogs = AuditLogs,
                StageCompletion = StageCompletion,
                WorkflowTodos = WorkflowTodos,
                LatestDispatches = LatestDispatches,
                LatestDiagnosticReport = LatestDiagnosticReport,
                CredentialSlots = CredentialSlots,
                ConfigGovernance = ConfigGovernance,
                StageReadiness = StageReadiness,
                IsTemplateUploadPending = IsTemplateUploadPending,
                TemplateUploadRetryCount = TemplateUploadRetryCount,
                TemplateUploadLastError = TemplateUploadLastError,
                TemplateUploadLastAttemptAt = TemplateUploadLastAttemptAt,
                ArtifactFiles = ArtifactFiles,
                ArtifactArchive = ArtifactArchive,
                ArtifactArchiveFileName = ArtifactArchiveFileName
            };
        }
    }
}
