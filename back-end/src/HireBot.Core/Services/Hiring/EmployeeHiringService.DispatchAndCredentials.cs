using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    private static IReadOnlyList<HiringCredentialSlotDto> UpsertCredentialSlot(
        IReadOnlyList<HiringCredentialSlotDto> existing,
        HiringCredentialSlotDto incoming)
    {
        var normalizedSlot = incoming.CredentialSlot.Trim();
        var result = existing
            .Where(item => !string.Equals(item.CredentialSlot, normalizedSlot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        result.Add(incoming with { CredentialSlot = normalizedSlot });
        return result
            .OrderBy(item => item.CredentialSlot, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildSecretRef(string credentialSlot)
    {
        var normalized = credentialSlot
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToUpperInvariant();
        return $"secret://hirebot/{normalized}";
    }

    private HiringRuntimeContext UpsertConfigGovernanceFile(
        HiringRuntimeContext runtimeContext,
        string configKey,
        string relativePath,
        string content,
        string? summary,
        IReadOnlyList<string>? affectedHandoffIds = null)
    {
        var normalizedConfigKey = configKey.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var impactedHandoffIds = (affectedHandoffIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (impactedHandoffIds.Length == 0)
        {
            impactedHandoffIds = runtimeContext.HandoffItems
                .Where(handoff => string.Equals(handoff.Status, HiringHandoffStatus.Confirmed, StringComparison.OrdinalIgnoreCase))
                .Select(handoff => handoff.HandoffId)
                .ToArray();
        }

        var packageFiles = runtimeContext.WorkingTemplatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);
        UpsertPackageFile(packageFiles, relativePath, content);

        var governanceFiles = (runtimeContext.ConfigGovernance?.Files ?? [])
            .Where(file => !string.Equals(file.ConfigKey, normalizedConfigKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => file.ConfigKey, StringComparer.OrdinalIgnoreCase);
        governanceFiles[normalizedConfigKey] = new HiringConfigGovernanceFileDto(
            ConfigKey: normalizedConfigKey,
            DisplayName: ResolveConfigDisplayName(normalizedConfigKey),
            RelativePath: relativePath,
            Content: content,
            Summary: summary?.Trim() ?? string.Empty,
            UpdatedAtUtc: now,
            AffectedHandoffIds: impactedHandoffIds);

        return runtimeContext with
        {
            WorkingTemplatePackage = runtimeContext.WorkingTemplatePackage with
            {
                PackageFiles = packageFiles.Values.ToArray()
            },
            ConfigGovernance = new HiringConfigGovernanceStateDto(
                Files: governanceFiles.Values
                    .OrderBy(file => file.ConfigKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PendingReviewHandoffIds: impactedHandoffIds,
                UpdatedAtUtc: now)
        };
    }

    private static IReadOnlyList<StageSkillMappingDto> BuildStageSkills(DiscoverySkillDefinition discoverySkill)
    {
        return discoverySkill.StageRules
            .Select(rule => new StageSkillMappingDto(
                Stage: rule.Stage,
                SkillName: rule.SkillName,
                RequiredFields: rule.RequiredFields,
                Description: rule.Description))
            .ToArray();
    }

    private static void EnsureHandoffIdsExist(
        IReadOnlyList<HiringWorkflowHandoffDto> existing,
        IReadOnlyList<string> handoffIds,
        string dispatchTarget)
    {
        var missingHandoffIds = handoffIds
            .Where(handoffId => existing.All(handoff => !string.Equals(handoff.HandoffId, handoffId, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingHandoffIds.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"dispatch {dispatchTarget} 引用的 handoff 不存在于当前 session metadata 中: {string.Join(", ", missingHandoffIds)}");
    }

    private static string NormalizeDispatchResultStatus(string? status, string fallbackStatus)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "success" => "success",
            "warning" => "warning",
            "failed" => "failed",
            "skipped" => "skipped",
            _ => fallbackStatus
        };
    }

    private static IReadOnlyList<HiringDispatchRecordDto> AppendDispatchRecord(
        IReadOnlyList<HiringDispatchRecordDto> existing,
        HiringDispatchRecordDto record)
    {
        var result = existing
            .Where(item => !string.Equals(item.DispatchId, record.DispatchId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        result.Add(record);
        return result
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.DispatchId, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
    }

    private static IReadOnlyList<HiringDispatchRecordDto> UpdateDispatchRecord(
        IReadOnlyList<HiringDispatchRecordDto> existing,
        string dispatchId,
        Func<HiringDispatchRecordDto, HiringDispatchRecordDto> updater)
    {
        return existing
            .Select(record => string.Equals(record.DispatchId, dispatchId, StringComparison.OrdinalIgnoreCase)
                ? updater(record)
                : record)
            .ToArray();
    }

    private string BuildDispatchConversationContent(
        HiringRuntimeContext runtimeContext,
        HiringDispatchCommand command,
        IReadOnlyList<HiringWorkflowHandoffDto> handoffItems)
    {
        var normalizedHandoffIds = handoffItems
            .Select(item => item.HandoffId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedHandoffs = handoffItems
            .Select(handoff => new
            {
                session_id = handoff.SessionId,
                workflow_id = handoff.WorkflowId,
                handoff_id = handoff.HandoffId,
                title = handoff.Title,
                kind = handoff.Kind,
                stage = handoff.Stage,
                target_skill = handoff.TargetSkill,
                intent = handoff.Intent,
                category = handoff.Category,
                payload = handoff.Payload,
                source = handoff.Source,
                acceptance = handoff.Acceptance,
                status = handoff.Status,
                fingerprint = handoff.Fingerprint,
                related_todos = handoff.RelatedHandoffIds,
                related_files = handoff.RelatedFiles,
                revision = handoff.Revision,
                created_at = handoff.CreatedAtUtc,
                updated_at = handoff.UpdatedAtUtc,
                dispatch_id = handoff.DispatchId,
                callback_summary = handoff.CallbackSummary
            })
            .ToArray();
        var payload = new
        {
            target = command.Target.Trim(),
            handoff_ids = normalizedHandoffIds,
            to = command.To?.Trim(),
            note = command.Note?.Trim(),
            mode = command.Mode?.Trim(),
            handoff_todos = selectedHandoffs,
            secure_credential_context = BuildSecureCredentialContext(runtimeContext, normalizedHandoffIds)
        };

        return $"<dispatch>{JsonSerializer.Serialize(payload, JsonOptions)}</dispatch>";
    }

    private object[] BuildSecureCredentialContext(
        HiringRuntimeContext runtimeContext,
        IReadOnlyList<string> handoffIds)
    {
        var relevantHandoffIds = handoffIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var boundSlots = runtimeContext.CredentialSlots
            .Where(slot =>
                string.Equals(slot.BindingStatus, HiringCredentialBindingStatus.Bound, StringComparison.OrdinalIgnoreCase) &&
                (relevantHandoffIds.Count == 0 || (!string.IsNullOrWhiteSpace(slot.HandoffId) && relevantHandoffIds.Contains(slot.HandoffId))))
            .ToArray();
        if (boundSlots.Length == 0)
        {
            return [];
        }

        var bindings = dbContext.HiringCredentialBindings
            .AsNoTracking()
            .Where(item => item.HireId == runtimeContext.HireId)
            .ToArray();
        var protector = dataProtectionProvider.CreateProtector(CredentialProtectorPurpose);

        return boundSlots
            .Select(slot =>
            {
                var entity = bindings.FirstOrDefault(item =>
                    string.Equals(item.CredentialSlot, slot.CredentialSlot, StringComparison.OrdinalIgnoreCase));
                if (entity is null)
                {
                    throw new InvalidOperationException($"凭据槽位 {slot.CredentialSlot} 已绑定但未找到密文记录");
                }

                return (object)new
                {
                    credential_slot = slot.CredentialSlot,
                    secret_ref = slot.SecretRef,
                    auth_kind = slot.AuthKind,
                    target_system = slot.TargetSystem,
                    handoff_id = slot.HandoffId,
                    secret_value = protector.Unprotect(entity.ProtectedSecret)
                };
            })
            .ToArray();
    }

    private HiringRuntimeContext ApplyDispatchCallbacks(
        HiringRuntimeContext runtimeContext,
        IReadOnlyList<HiringDispatchCallbackPayload> callbacks,
        string? dispatchId = null,
        string? fallbackTarget = null)
    {
        var updatedRuntimeContext = runtimeContext;
        foreach (var callback in callbacks)
        {
            updatedRuntimeContext = ApplyDispatchCallback(updatedRuntimeContext, callback, dispatchId, fallbackTarget);
        }

        return updatedRuntimeContext;
    }

    private HiringRuntimeContext ApplyDispatchCallback(
        HiringRuntimeContext runtimeContext,
        HiringDispatchCallbackPayload callback,
        string? dispatchId,
        string? fallbackTarget = null)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedTarget = string.IsNullOrWhiteSpace(callback.SourceDispatchTarget)
            ? fallbackTarget?.Trim() ?? "unknown"
            : callback.SourceDispatchTarget.Trim();
        var callbackHandoffIds = callback.HandoffIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var callbackResultHandoffIds = callback.TodoResults
            .Where(item => !string.IsNullOrWhiteSpace(item.HandoffId))
            .Select(item => item.HandoffId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EnsureHandoffIdsExist(runtimeContext.HandoffItems, callbackHandoffIds, normalizedTarget);
        EnsureHandoffIdsExist(runtimeContext.HandoffItems, callbackResultHandoffIds, normalizedTarget);
        var missingResultHandoffIds = callbackHandoffIds
            .Where(handoffId => !callbackResultHandoffIds.Contains(handoffId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingResultHandoffIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"dispatch_callback 缺少这些 handoff 的 todo_results: {string.Join(", ", missingResultHandoffIds)}");
        }
        var packageFiles = runtimeContext.WorkingTemplatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);
        var artifactFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var artifactDtos = new Dictionary<string, HiringDispatchArtifactDto>(StringComparer.OrdinalIgnoreCase);

        void MergeArtifact(HiringDispatchCallbackArtifactPayload artifactPayload)
        {
            if (!TryNormalizeArtifactPath(artifactPayload.Path, out var normalizedPath, out var pathError))
            {
                throw new InvalidOperationException(pathError);
            }

            if (!HiringWorkflowSupport.IsAllowedArtifactPath(normalizedPath))
            {
                throw new InvalidOperationException($"artifact path 不允许回写: {normalizedPath}");
            }

            var bytes = HiringWorkflowSupport.DecodeArtifactContent(artifactPayload);
            var actualSha = HiringWorkflowSupport.ComputeSha256(bytes);
            if (!string.Equals(actualSha, artifactPayload.Sha256?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"artifact sha256 校验失败: {normalizedPath}");
            }

            if (ShouldInspectSensitiveContent(normalizedPath) &&
                HiringWorkflowSupport.ContainsSensitiveValue(Encoding.UTF8.GetString(bytes)))
            {
                throw new InvalidOperationException($"artifact 检测到疑似明文凭据，已拒绝回写: {normalizedPath}");
            }

            if (packageFiles.TryGetValue(normalizedPath, out var existingFile) &&
                !string.Equals(existingFile.ContentHash, actualSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"artifact path 冲突，禁止覆盖已有文件: {normalizedPath}");
            }

            if (artifactFiles.TryGetValue(normalizedPath, out var existingBytes) &&
                !string.Equals(HiringWorkflowSupport.ComputeSha256(existingBytes), actualSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"artifact path 冲突，禁止重复写入不同内容: {normalizedPath}");
            }

            packageFiles[normalizedPath] = new TemplatePackageFileAsset(normalizedPath, bytes, actualSha);
            artifactFiles[normalizedPath] = bytes;
            artifactDtos[normalizedPath] = new HiringDispatchArtifactDto(
                Path: normalizedPath,
                Kind: string.IsNullOrWhiteSpace(artifactPayload.Kind) ? "file" : artifactPayload.Kind.Trim(),
                Encoding: string.IsNullOrWhiteSpace(artifactPayload.Encoding) ? "plain" : artifactPayload.Encoding.Trim(),
                Sha256: actualSha);
        }

        foreach (var artifact in callback.Artifacts)
        {
            MergeArtifact(artifact);
        }

        foreach (var artifact in callback.TodoResults.SelectMany(item => item.Artifacts))
        {
            MergeArtifact(artifact);
        }

        var todoResults = callback.TodoResults
            .Select(item => new HiringDispatchHandoffResultDto(
                HandoffId: item.HandoffId,
                Status: NormalizeDispatchResultStatus(item.Status, "failed"),
                Artifacts: item.Artifacts
                    .Select(artifact =>
                    {
                        if (!TryNormalizeArtifactPath(artifact.Path, out var normalizedPath, out _))
                        {
                            normalizedPath = artifact.Path;
                        }

                        return artifactDtos.TryGetValue(normalizedPath, out var dto)
                            ? dto
                            : new HiringDispatchArtifactDto(
                                Path: normalizedPath,
                                Kind: string.IsNullOrWhiteSpace(artifact.Kind) ? "file" : artifact.Kind.Trim(),
                                Encoding: string.IsNullOrWhiteSpace(artifact.Encoding) ? "plain" : artifact.Encoding.Trim(),
                                Sha256: artifact.Sha256.Trim());
                    })
                    .ToArray(),
                Errors: item.Errors))
            .ToArray();

        var updatedCredentialSlots = runtimeContext.CredentialSlots;
        foreach (var credentialSlot in callback.TodoResults
                     .SelectMany(item => item.CredentialSlots ?? [])
                     .Where(slot => !string.IsNullOrWhiteSpace(slot.CredentialSlot)))
        {
            updatedCredentialSlots = UpsertCredentialSlot(
                updatedCredentialSlots,
                credentialSlot with
                {
                    BindingStatus = NormalizeCredentialBindingStatus(credentialSlot.BindingStatus),
                    UpdatedAtUtc = credentialSlot.UpdatedAtUtc == default ? now : credentialSlot.UpdatedAtUtc
                });
        }

        var resolvedDispatchId = string.IsNullOrWhiteSpace(dispatchId) ? $"dispatch-{Guid.NewGuid():N}" : dispatchId;
        var updatedDispatches = UpdateDispatchRecord(
            runtimeContext.LatestDispatches,
            resolvedDispatchId,
            record => record with
            {
                Target = normalizedTarget,
                Status = NormalizeDispatchStatus(callback.Status),
                HandoffIds = callback.HandoffIds.Count == 0 ? record.HandoffIds : callback.HandoffIds,
                Note = record.Note,
                UserSummary = string.IsNullOrWhiteSpace(callback.UserSummary) ? record.UserSummary : callback.UserSummary.Trim(),
                Artifacts = artifactDtos.Values.ToArray(),
                TodoResults = todoResults,
                CompletedAtUtc = now,
                Errors = callback.Errors
            });

        if (!updatedDispatches.Any(item => string.Equals(item.DispatchId, resolvedDispatchId, StringComparison.OrdinalIgnoreCase)))
        {
            updatedDispatches = AppendDispatchRecord(
                updatedDispatches,
                new HiringDispatchRecordDto(
                    DispatchId: resolvedDispatchId,
                    Target: normalizedTarget,
                    Status: NormalizeDispatchStatus(callback.Status),
                    HandoffIds: callback.HandoffIds,
                    To: null,
                    Note: null,
                    UserSummary: string.IsNullOrWhiteSpace(callback.UserSummary) ? null : callback.UserSummary.Trim(),
                    Artifacts: artifactDtos.Values.ToArray(),
                    TodoResults: todoResults,
                    CreatedAtUtc: now,
                    CompletedAtUtc: now,
                    Errors: callback.Errors));
        }

        return runtimeContext with
        {
            WorkingTemplatePackage = runtimeContext.WorkingTemplatePackage with
            {
                PackageFiles = packageFiles.Values.ToArray()
            },
            CredentialSlots = updatedCredentialSlots,
            LatestDispatches = updatedDispatches
        };
    }

    private static string NormalizeCredentialBindingStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            HiringCredentialBindingStatus.Bound => HiringCredentialBindingStatus.Bound,
            HiringCredentialBindingStatus.NotRequired => HiringCredentialBindingStatus.NotRequired,
            HiringCredentialBindingStatus.Failed => HiringCredentialBindingStatus.Failed,
            _ => HiringCredentialBindingStatus.Pending
        };
    }

    private static string NormalizeDispatchStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "completed";
        }

        return status.Trim().ToLowerInvariant();
    }

    private static bool ShouldInspectSensitiveContent(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".toml", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveConfigDisplayName(string configKey)
    {
        return configKey switch
        {
            HiringConfigFileKeys.Soul => "SOUL.md",
            HiringConfigFileKeys.Identity => "IDENTITY.md",
            HiringConfigFileKeys.Agents => "AGENTS.md",
            _ => configKey
        };
    }

    private static HiringStagePreviewDto EnrichStagePreview(
        HiringStagePreviewDto preview,
        DiscoverySkillDefinition discoverySkill,
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string currentStage,
        string collectionPhase,
        IReadOnlyDictionary<string, string?> structuredData)
    {
        var currentRule = discoverySkill.StageRules.FirstOrDefault(rule =>
            string.Equals(rule.Stage, currentStage, StringComparison.OrdinalIgnoreCase));
        var currentCompletion = stageCompletion.FirstOrDefault(item =>
            string.Equals(item.Stage, currentStage, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<string> riskNotes;
        if (string.Equals(collectionPhase, HiringCollectionPhase.ReadyForFinalize, StringComparison.OrdinalIgnoreCase))
        {
            riskNotes = ["所有 discovery 阶段已满足，可执行 finalize 生成实例交付物。"];
        }
        else if (currentCompletion is not null && currentCompletion.BlockingFields.Count > 0)
        {
            riskNotes = [$"当前阶段仍缺少字段：{string.Join("、", currentCompletion.BlockingFields)}"];
        }
        else
        {
            riskNotes = ["当前阶段字段已齐全，可进入下一阶段。"];
        }

        return preview with
        {
            Stage = currentStage,
            SkillName = currentRule?.SkillName ?? preview.SkillName,
            StructuredData = structuredData,
            MissingFields = currentCompletion?.BlockingFields ?? preview.MissingFields,
            RiskNotes = riskNotes,
            ReadyForAudit = currentCompletion?.ReadyForNextStage ?? preview.ReadyForAudit
        };
    }

}
