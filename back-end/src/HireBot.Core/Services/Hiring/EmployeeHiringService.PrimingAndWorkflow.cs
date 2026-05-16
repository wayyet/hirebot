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
    internal static string BuildReferenceTemplatePrimingContent(
        EmployeeTemplateDefinition template,
        TemplatePackageDefinition referenceTemplatePackage,
        string referenceTemplatePrimingPrompt)
    {
        var summaryMarkdown = BuildReferenceTemplateSummaryMarkdown(template, referenceTemplatePackage);
        return $"{referenceTemplatePrimingPrompt}{Environment.NewLine}{Environment.NewLine}{summaryMarkdown}{Environment.NewLine}{Environment.NewLine}请直接基于上面的摘要进入分析和追问；除非确有必要，不要让用户重复提供你已经收到的资料内容。";
    }

    private static IReadOnlyList<HiringConversationMaterialDto> BuildReferenceTemplatePrimingMaterials(
        PersistedSourceZipInfo? referenceSourceZip)
    {
        var materials = new List<HiringConversationMaterialDto>();
        if (referenceSourceZip is not null && !string.IsNullOrWhiteSpace(referenceSourceZip.StoragePath))
        {
            materials.Add(new HiringConversationMaterialDto
            {
                Type = "file",
                Name = referenceSourceZip.FileName,
                ContentHash = referenceSourceZip.ContentHash,
                Size = referenceSourceZip.SizeBytes,
                MimeType = "application/zip",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["storagePath"] = referenceSourceZip.StoragePath,
                    ["archiveFormat"] = "zip",
                    ["referenceType"] = "template-source-archive"
                }
            });
        }

        return materials;
    }

    private static string BuildReferenceTemplateSummaryMarkdown(
        EmployeeTemplateDefinition template,
        TemplatePackageDefinition referenceTemplatePackage)
    {
        var useCases = CollectReferenceTemplateUseCases(template, referenceTemplatePackage);
        var skillNames = referenceTemplatePackage.RequiredSkills
            .Select(skill => skill.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ontologyNames = referenceTemplatePackage.OntologySlices
            .Select(slice => slice.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reuseHints = BuildReferenceTemplateReuseHints(useCases, skillNames, ontologyNames, referenceTemplatePackage);

        var builder = new StringBuilder();
        builder.AppendLine("# 参考模板摘要");
        builder.AppendLine();
        builder.AppendLine("## 模板基本信息");
        builder.AppendLine($"- 模板 ID: {template.TemplateId}");
        builder.AppendLine($"- 模板名称: {template.Name}");
        builder.AppendLine($"- 标语: {template.Tagline}");
        builder.AppendLine($"- 描述: {template.Description}");
        builder.AppendLine();
        builder.AppendLine("## Use Cases");
        AppendMarkdownList(builder, useCases, "未显式声明 use case");
        builder.AppendLine();
        builder.AppendLine("## Skills");
        AppendMarkdownList(builder, skillNames, "未解析到内置技能");
        builder.AppendLine();
        builder.AppendLine("## Ontology");
        AppendMarkdownList(builder, ontologyNames, "未解析到 ontology 切片");
        builder.AppendLine();
        builder.AppendLine("## 版本信息");
        builder.AppendLine($"- package_id: {referenceTemplatePackage.PackageId}");
        builder.AppendLine($"- package_version: {referenceTemplatePackage.PackageVersion}");
        builder.AppendLine($"- package_hash: {referenceTemplatePackage.PackageHash}");
        builder.AppendLine();
        builder.AppendLine("## 建议复用点");
        AppendMarkdownList(builder, reuseHints, "优先关注模板的业务边界、核心技能拆分和 ontology 命名约定");

        return builder.ToString().Trim();
    }

    private static string[] CollectReferenceTemplateUseCases(
        EmployeeTemplateDefinition template,
        TemplatePackageDefinition referenceTemplatePackage)
    {
        var manifestUseCases = ParseManifestStringArray(referenceTemplatePackage.ManifestJson, "use_cases");
        if (manifestUseCases.Length > 0)
        {
            return manifestUseCases;
        }

        return template.InScope
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BuildReferenceTemplateReuseHints(
        IReadOnlyList<string> useCases,
        IReadOnlyList<string> skillNames,
        IReadOnlyList<string> ontologyNames,
        TemplatePackageDefinition referenceTemplatePackage)
    {
        var result = new List<string>();
        if (useCases.Count > 0)
        {
            result.Add($"优先复用业务场景边界：{useCases[0]}");
        }

        if (skillNames.Count > 0)
        {
            result.Add($"优先复用技能拆分方式：{string.Join("、", skillNames.Take(3))}");
        }

        if (ontologyNames.Count > 0)
        {
            result.Add($"优先复用 ontology 命名和切片粒度：{string.Join("、", ontologyNames.Take(3))}");
        }

        var manifestTags = ParseManifestStringArray(referenceTemplatePackage.ManifestJson, "tags");
        if (manifestTags.Length > 0)
        {
            result.Add($"保留模板标签语义，作为后续配置和定位参考：{string.Join("、", manifestTags.Take(4))}");
        }

        return result.Count == 0
            ? ["优先复用模板的业务边界、关键文件组织和技能命名方式"]
            : result.ToArray();
    }

    private static string[] ParseManifestStringArray(string manifestJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(manifestJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return [];
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var single = property.GetString();
                return string.IsNullOrWhiteSpace(single) ? [] : [single.Trim()];
            }

            if (property.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static void AppendMarkdownList(StringBuilder builder, IReadOnlyList<string> values, string fallback)
    {
        if (values.Count == 0)
        {
            builder.AppendLine($"- {fallback}");
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }

    private static IReadOnlyList<HiringConversationMessageDto> AppendMessages(
        IReadOnlyList<HiringConversationMessageDto> existing,
        params HiringConversationMessageDto[] appended)
    {
        if (appended.Length == 0)
        {
            return existing;
        }

        return existing
            .Concat(appended)
            .Where(message => message is not null)
            .OrderBy(message => message.CreatedAt)
            .ToArray();
    }

    private HiringRuntimeContext ApplyWorkflowProgress(HiringRuntimeContext runtimeContext)
    {
        // Handoff 驱动的阶段评估已停用：新方案由沙箱 skill 直接向前端传递业务数据，
        // CollectionPhase / CurrentStage 由外部显式设置，不再基于 HandoffItems 自动推进。
        return runtimeContext with
        {
            StructuredData = NormalizeStructuredData(runtimeContext.StructuredData),
            CredentialSlots = runtimeContext.CredentialSlots
                .OrderBy(item => item.CredentialSlot, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private HiringRuntimeContext ApplyAssistantReply(
        HiringRuntimeContext runtimeContext,
        ParsedHiringAssistantReply parsedReply)
    {
        var updatedRuntimeContext = runtimeContext with
        {
            LatestDiagnosticReport = parsedReply.DiagnosticReport ?? runtimeContext.LatestDiagnosticReport
        };

        foreach (var configFile in parsedReply.ConfigGovernanceFiles)
        {
            updatedRuntimeContext = UpsertConfigGovernanceFile(
                updatedRuntimeContext,
                configFile.ConfigKey,
                configFile.RelativePath,
                configFile.Content,
                configFile.Summary,
                configFile.AffectedHandoffIds);
        }

        return updatedRuntimeContext;
    }

    private void LogParsedAssistantReply(
        HiringRuntimeContext runtimeContext,
        ParsedHiringAssistantReply parsedReply)
    {
        logger.LogInformation(
            "Parsed assistant reply. HireId={HireId}, SessionId={SessionId}, CurrentStage={CurrentStage}, DispatchCount={DispatchCount}, DispatchCallbackCount={DispatchCallbackCount}, HasDiagnosticReport={HasDiagnosticReport}, ConfigGovernanceFileCount={ConfigGovernanceFileCount}, VisibleContentLength={VisibleContentLength}",
            runtimeContext.HireId,
            runtimeContext.SessionId,
            runtimeContext.CurrentStage,
            parsedReply.DispatchCommands.Count,
            parsedReply.DispatchCallbacks.Count,
            parsedReply.DiagnosticReport is not null,
            parsedReply.ConfigGovernanceFiles.Count,
            parsedReply.VisibleContent.Length);
    }

    private async Task<HiringRuntimeContext> ExecuteDispatchCommandsAsync(
        HiringRuntimeContext runtimeContext,
        IReadOnlyList<HiringDispatchCommand> dispatchCommands,
        CancellationToken cancellationToken)
    {
        if (dispatchCommands.Count == 0)
        {
            return runtimeContext;
        }

        var updatedRuntimeContext = runtimeContext;
        foreach (var command in dispatchCommands)
        {
            if (string.IsNullOrWhiteSpace(command.Target))
            {
                throw new InvalidOperationException("dispatch target 不能为空");
            }

            var normalizedTarget = command.Target.Trim();
            if (string.Equals(normalizedTarget, "stage_transition", StringComparison.OrdinalIgnoreCase))
            {
                updatedRuntimeContext = ExecuteLocalStageTransition(updatedRuntimeContext, command);
                continue;
            }

            var normalizedHandoffIds = NormalizeHandoffIds(command.HandoffIds);
            if (normalizedHandoffIds.Length == 0)
            {
                throw new InvalidOperationException($"dispatch {normalizedTarget} 必须提供至少一个 handoff_id");
            }

            var dispatchHandoffs = ResolveDispatchHandoffs(
                updatedRuntimeContext.HandoffItems,
                normalizedHandoffIds,
                normalizedTarget);
            var dispatchId = $"dispatch-{Guid.NewGuid():N}";
            var createdAt = DateTimeOffset.UtcNow;
            updatedRuntimeContext = updatedRuntimeContext with
            {
                LatestDispatches = AppendDispatchRecord(
                    updatedRuntimeContext.LatestDispatches,
                    new HiringDispatchRecordDto(
                        DispatchId: dispatchId,
                        Target: normalizedTarget,
                        Status: "running",
                        HandoffIds: normalizedHandoffIds,
                        To: null,
                        Note: command.Note?.Trim(),
                        UserSummary: null,
                        Artifacts: [],
                        TodoResults: [],
                        CreatedAtUtc: createdAt,
                        CompletedAtUtc: null,
                        Errors: []))
            };

            var dispatchContent = BuildDispatchConversationContent(updatedRuntimeContext, command, dispatchHandoffs);
            var dispatchResponse = await SendSandboxConversationMessageAsync(
                updatedRuntimeContext,
                dispatchContent,
                [],
                cancellationToken);
            if (!dispatchResponse.Success || dispatchResponse.Data is null)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(dispatchResponse.Message)
                        ? $"dispatch {normalizedTarget} 执行失败"
                        : dispatchResponse.Message);
            }

            var parsedReply = HiringWorkflowSupport.ParseAssistantReply(dispatchResponse.Data.AssistantMessage.Content);
            if (parsedReply.DispatchCallbacks.Count == 0)
            {
                throw new InvalidOperationException($"dispatch {normalizedTarget} 未返回 dispatch_callback");
            }

            updatedRuntimeContext = updatedRuntimeContext with
            {
                SessionId = dispatchResponse.Data.SessionId
            };
            updatedRuntimeContext = ApplyAssistantReply(updatedRuntimeContext, parsedReply);
            updatedRuntimeContext = ApplyDispatchCallbacks(
                updatedRuntimeContext,
                parsedReply.DispatchCallbacks,
                dispatchId,
                normalizedTarget);
        }

        return updatedRuntimeContext;
    }

    private static string[] NormalizeHandoffIds(IReadOnlyList<string>? handoffIds)
    {
        if (handoffIds is null || handoffIds.Count == 0)
        {
            return [];
        }

        return handoffIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HiringWorkflowHandoffDto[] ResolveDispatchHandoffs(
        IReadOnlyList<HiringWorkflowHandoffDto> existing,
        IReadOnlyList<string> handoffIds,
        string dispatchTarget)
    {
        if (!string.Equals(dispatchTarget, "ontology-extraction", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dispatchTarget, "skill-generation", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dispatchTarget, "external-config", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"dispatch target 不受支持: {dispatchTarget}");
        }

        EnsureHandoffIdsExist(existing, handoffIds, dispatchTarget);
        var selectedHandoffs = existing
            .Where(item => handoffIds.Contains(item.HandoffId, StringComparer.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.HandoffId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var invalidHandoffIds = selectedHandoffs
            .Where(item =>
                !string.Equals(item.Kind, HiringHandoffKind.HandoffTodo, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.TargetSkill, dispatchTarget, StringComparison.OrdinalIgnoreCase) ||
                (!string.Equals(item.Status, HiringHandoffStatus.ReadyToDispatch, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(item.Status, HiringHandoffStatus.Dirty, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.HandoffId)
            .ToArray();
        if (invalidHandoffIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"dispatch {dispatchTarget} 只允许处理 status=ready_to_dispatch|dirty 且 target_skill 匹配的 handoff: {string.Join(", ", invalidHandoffIds)}");
        }

        return selectedHandoffs;
    }

    private HiringRuntimeContext ExecuteLocalStageTransition(
        HiringRuntimeContext runtimeContext,
        HiringDispatchCommand command)
    {
        if (!string.Equals(command.To?.Trim(), "instance_packaging", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("stage_transition 仅支持 to=instance_packaging");
        }

        var normalizedHandoffIds = NormalizeHandoffIds(command.HandoffIds);
        if (normalizedHandoffIds.Length > 0)
        {
            throw new InvalidOperationException("stage_transition 不允许携带 handoff_ids");
        }

        // Handoff 就绪性检查已停用：不再通过 HandoffItems 校验阶段出口条件。

        var now = DateTimeOffset.UtcNow;
        return runtimeContext with
        {
            CurrentStage = HiringCollectionStage.ReadyForPackaging,
            CollectionPhase = HiringCollectionPhase.ReadyForFinalize,
            LatestDispatches = AppendDispatchRecord(
                runtimeContext.LatestDispatches,
                new HiringDispatchRecordDto(
                    DispatchId: $"dispatch-{Guid.NewGuid():N}",
                    Target: "stage_transition",
                    Status: "completed",
                    HandoffIds: [],
                    To: "instance_packaging",
                    Note: command.Note?.Trim(),
                    UserSummary: "Workflow 已切换到 instance_packaging",
                    Artifacts: [],
                    TodoResults: [],
                    CreatedAtUtc: now,
                    CompletedAtUtc: now,
                    Errors: []))
        };
    }

    private static string NormalizeRequestedStage(string stage)
    {
        return stage.Trim().ToLowerInvariant() switch
        {
            "goal" or "material" => HiringCollectionStage.Material,
            "scenario" or "skill" => HiringCollectionStage.Skill,
            "systems" or "gaps" or "external" => HiringCollectionStage.External,
            "package" or "ready_for_packaging" => HiringCollectionStage.ReadyForPackaging,
            _ => stage.Trim()
        };
    }

}
