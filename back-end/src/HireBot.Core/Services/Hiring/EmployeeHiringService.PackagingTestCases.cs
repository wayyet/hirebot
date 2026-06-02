using System.Text;
using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Internal;
using HireBot.Abstraction.Models.Sandbox;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    private const string PackagingTestCasesRelativePath = "testcases/evaluation-test-cases.json";
    private const string PackagingTestCasesOntologyCopyPath = "ontology/hiring-session/evaluation-test-cases.json";
    private const string PackagingTestCasesSourcesIndexPath = "ontology/hiring-session/testcases-sources-index.json";
    private const string PackagingTestCasesHistoryDerivedPath = "ontology/hiring-session/testcases-sources/history-derived.json";
    private const string PackagingTestCasesMaterialsDerivedPath = "ontology/hiring-session/testcases-sources/materials-derived.json";
    private const string PackagingTestCasesTemplateDerivedPath = "ontology/hiring-session/testcases-sources/template-derived.json";
    private const string PackagingTestCasesSourceMerged = "packaging-merged";
    private const string PackagingTestCasesSourceHistoryLlm = "kingcrab-history-llm";
    private const string PackagingTestCasesSourceFallback = "packaging-fallback";
    private const string PackagingTestCasesSkillTarget = "packaging-test-cases";

    private static readonly JsonSerializerOptions PackagingTestCasesJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>
    /// 收集阶段是否尚未进入打包前 testcase 阶段。
    /// </summary>
    internal static bool IsCollectionStageBeforeReadyForPackaging(string? currentStage)
    {
        if (string.IsNullOrWhiteSpace(currentStage))
        {
            return true;
        }

        return string.Equals(currentStage, HiringCollectionStage.Material, StringComparison.OrdinalIgnoreCase)
               || string.Equals(currentStage, HiringCollectionStage.Skill, StringComparison.OrdinalIgnoreCase)
               || string.Equals(currentStage, HiringCollectionStage.External, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 是否应在沙箱调用 package_workspace 之前写入 testcases/。
    /// </summary>
    internal static bool ShouldStagePackagingTestCases(HiringRuntimeContext runtimeContext, string? userMessage)
    {
        if (string.Equals(runtimeContext.CurrentStage, HiringCollectionStage.ReadyForPackaging, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        return PackagingIntentSupport.IsPackagingIntent(userMessage);
    }

    /// <summary>
    /// 构建降级 testcase JSON（demo 结构，test_cases 为空）。
    /// </summary>
    internal static bool TryBuildPackagingTestCasesPlaceholder(out string testCasesJson)
    {
        testCasesJson = BuildPackagingTestCasesPlaceholderJson();
        return true;
    }

    internal static string BuildPackagingTestCasesPlaceholderJson()
    {
        var payload = new
        {
            description = "雇佣会话评估测试用例（降级）",
            role = "digital_employee",
            industry = "general",
            generated_at = DateTimeOffset.UtcNow,
            source = PackagingTestCasesSourceFallback,
            test_cases = Array.Empty<object>()
        };

        return JsonSerializer.Serialize(payload, PackagingTestCasesJsonOptions);
    }

    internal static SandboxWorkspaceUploadRequestDto BuildPackagingTestCaseUploadRequest(
        HiringRuntimeContext runtimeContext,
        byte[] content)
    {
        return BuildPackagingWorkspaceUploadRequest(runtimeContext, "testcases", "evaluation-test-cases.json", content);
    }

    internal static SandboxWorkspaceUploadRequestDto BuildPackagingWorkspaceUploadRequest(
        HiringRuntimeContext runtimeContext,
        string targetDir,
        string fileName,
        byte[] content)
    {
        return new SandboxWorkspaceUploadRequestDto
        {
            ScopeType = SandboxScopeTypes.Hire,
            ScopeKey = runtimeContext.HireId,
            SandboxRole = "hiring",
            OwnerSubject = runtimeContext.OwnerSubject,
            SandboxId = runtimeContext.SandboxId,
            TargetDir = targetDir,
            FileName = fileName,
            Content = content,
            ContentType = "application/json"
        };
    }

    internal static SandboxSessionDetailRequestDto BuildPackagingSessionDetailRequest(HiringRuntimeContext runtimeContext)
    {
        return new SandboxSessionDetailRequestDto
        {
            ScopeType = SandboxScopeTypes.Hire,
            ScopeKey = runtimeContext.HireId,
            SandboxRole = "hiring",
            OwnerSubject = runtimeContext.OwnerSubject,
            TenantId = runtimeContext.TenantId,
            OperatorId = runtimeContext.OperatorId,
            SessionKey = "default",
            SandboxId = runtimeContext.SandboxId
        };
    }

    /// <summary>
    /// 通过沙箱 packaging-test-cases Skill 生成 testcase JSON 包。
    /// </summary>
    internal async Task<(bool Success, PackagingTestCasesBundle? Bundle)> InvokePackagingTestCasesSkillAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        var sessionId = runtimeContext.SessionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            logger.LogWarning(
                "[Hiring] Packaging testcase skill proceeding without SessionId; history transcript will be empty. HireId={HireId}",
                runtimeContext.HireId);
        }

        var todoFilesRoot = HireBotPathResolver.ResolveTodoFilesRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:EvaluationResourceRoot"]);
        var uploadedMaterialFiles = await PackagingTestCaseMaterialLoader.LoadAsync(
            dbContext,
            runtimeContext.HireId,
            sessionId,
            [todoFilesRoot],
            cancellationToken);
        var templatePackageFiles = PackagingTestCaseTemplateSnapshotBuilder.Build(
            runtimeContext.WorkingTemplatePackage.PackageFiles);

        IReadOnlyList<HiringConversationMessageDto> sessionMessages = [];
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var sessionDetailResult = await sandboxService.GetSessionDetailAsync(
                BuildPackagingSessionDetailRequest(runtimeContext),
                cancellationToken);
            if (sessionDetailResult.Success && sessionDetailResult.Data is not null)
            {
                sessionMessages = sessionDetailResult.Data.Messages;
            }
            else
            {
                logger.LogWarning(
                    "[Hiring] Failed to load KingCrab session history for packaging testcases. HireId={HireId}, SessionId={SessionId}, Code={Code}, Message={Message}",
                    runtimeContext.HireId,
                    sessionId,
                    sessionDetailResult.Code,
                    sessionDetailResult.Message);
            }
        }

        var transcript = PackagingTestCasesJsonValidator.PrepareHistoryTranscript(sessionMessages);
        if (transcript.Count == 0 &&
            uploadedMaterialFiles.Count == 0 &&
            templatePackageFiles.Count == 0)
        {
            logger.LogWarning(
                "[Hiring] Packaging testcase inputs are empty (history/materials/template). HireId={HireId}, SessionId={SessionId}",
                runtimeContext.HireId,
                runtimeContext.SessionId);
            return (false, null);
        }

        var invokePayload = new PackagingTestCasesInvokePayload(
            sessionId,
            runtimeContext.TemplateName,
            runtimeContext.StructuredData,
            transcript,
            uploadedMaterialFiles,
            templatePackageFiles);
        var invokeContent =
            $"<invoke_packaging_testcases>{PackagingTestCasesJsonValidator.SerializeInvokePayload(invokePayload)}</invoke_packaging_testcases>";

        var skillResponse = await SendSandboxConversationMessageAsync(
            runtimeContext,
            invokeContent,
            [],
            cancellationToken);
        if (!skillResponse.Success || skillResponse.Data is null)
        {
            logger.LogWarning(
                "[Hiring] Packaging testcase skill invoke failed. HireId={HireId}, SessionId={SessionId}, Code={Code}, Message={Message}",
                runtimeContext.HireId,
                runtimeContext.SessionId,
                skillResponse.Code,
                skillResponse.Message);
            return (false, null);
        }

        var parsedReply = HiringWorkflowSupport.ParseAssistantReply(skillResponse.Data.AssistantMessage.Content);
        var callback = parsedReply.DispatchCallbacks.FirstOrDefault(item =>
            string.Equals(item.SourceDispatchTarget, PackagingTestCasesSkillTarget, StringComparison.OrdinalIgnoreCase));
        if (callback is null)
        {
            logger.LogWarning(
                "[Hiring] Packaging testcase skill returned no dispatch_callback. HireId={HireId}, SessionId={SessionId}",
                runtimeContext.HireId,
                runtimeContext.SessionId);
            return (false, null);
        }

        if (!PackagingTestCasesJsonValidator.TryExtractPackagingTestCasesBundle(callback, out var bundle))
        {
            logger.LogWarning(
                "[Hiring] Packaging testcase skill callback failed bundle validation. HireId={HireId}, SessionId={SessionId}",
                runtimeContext.HireId,
                runtimeContext.SessionId);
            return (false, null);
        }

        var normalizedBundle = EnsureBundleMetadata(bundle);
        logger.LogInformation(
            "[Hiring] Generated packaging testcases via packaging-test-cases skill. HireId={HireId}, SessionId={SessionId}, HistoryTurns={HistoryTurns}, MaterialFiles={MaterialFiles}, TemplateFiles={TemplateFiles}, Source={Source}",
            runtimeContext.HireId,
            runtimeContext.SessionId,
            transcript.Count,
            uploadedMaterialFiles.Count,
            templatePackageFiles.Count,
            normalizedBundle.Source);

        return (true, normalizedBundle);
    }

    /// <summary>
    /// 将 testcase 上传到雇佣沙箱，并同步写入 WorkingTemplatePackage（幂等）。
    /// </summary>
    private async Task<HiringRuntimeContext> EnsurePackagingTestCasesStagedAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (runtimeContext.PackagingTestCasesStaged)
        {
            return runtimeContext;
        }

        var (skillSuccess, bundle) = await InvokePackagingTestCasesSkillAsync(runtimeContext, cancellationToken);
        if (!skillSuccess || bundle is null)
        {
            if (!TryBuildPackagingTestCasesPlaceholder(out var placeholderJson))
            {
                return runtimeContext;
            }

            logger.LogWarning(
                "[Hiring] Packaging testcase generation fell back to empty demo structure. HireId={HireId}, SessionId={SessionId}",
                runtimeContext.HireId,
                runtimeContext.SessionId);

            bundle = new PackagingTestCasesBundle(
                placeholderJson,
                SourcesIndexJson: string.Empty,
                HistoryDerivedJson: string.Empty,
                MaterialsDerivedJson: string.Empty,
                TemplateDerivedJson: string.Empty,
                PackagingTestCasesSourceFallback);
        }

        var uploadSucceeded = await UploadPackagingTestCasesBundleAsync(runtimeContext, bundle, cancellationToken);
        if (!uploadSucceeded)
        {
            return runtimeContext;
        }

        runtimeContext = ApplyPackagingTestCasesToWorkingPackage(runtimeContext, bundle);
        runtimeContext = runtimeContext with { PackagingTestCasesStaged = true };

        logger.LogInformation(
            "[Hiring] Packaging testcases staged to sandbox before package_workspace. HireId={HireId}, SandboxId={SandboxId}, Path={Path}",
            runtimeContext.HireId,
            runtimeContext.SandboxId,
            PackagingTestCasesRelativePath);

        return runtimeContext;
    }

    private async Task<bool> UploadPackagingTestCasesBundleAsync(
        HiringRuntimeContext runtimeContext,
        PackagingTestCasesBundle bundle,
        CancellationToken cancellationToken)
    {
        var filesToUpload = BuildPackagingUploadEntries(bundle);
        foreach (var entry in filesToUpload)
        {
            var uploadRequest = BuildPackagingWorkspaceUploadRequest(
                runtimeContext,
                entry.TargetDir,
                entry.FileName,
                Encoding.UTF8.GetBytes(entry.Json));
            var uploadResult = await sandboxService.UploadWorkspaceFileAsync(uploadRequest, cancellationToken);
            if (!uploadResult.Success)
            {
                logger.LogWarning(
                    "[Hiring] Failed to stage packaging testcases to sandbox. HireId={HireId}, SandboxId={SandboxId}, TargetDir={TargetDir}, FileName={FileName}, Code={Code}, Message={Message}",
                    runtimeContext.HireId,
                    runtimeContext.SandboxId,
                    entry.TargetDir,
                    entry.FileName,
                    uploadResult.Code,
                    uploadResult.Message);
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<PackagingUploadEntry> BuildPackagingUploadEntries(PackagingTestCasesBundle bundle)
    {
        var entries = new List<PackagingUploadEntry>
        {
            new("testcases", "evaluation-test-cases.json", bundle.MergedJson)
        };

        if (!string.IsNullOrWhiteSpace(bundle.SourcesIndexJson))
        {
            entries.Add(new("ontology/hiring-session", "testcases-sources-index.json", bundle.SourcesIndexJson));
        }

        if (!string.IsNullOrWhiteSpace(bundle.HistoryDerivedJson))
        {
            entries.Add(new("ontology/hiring-session/testcases-sources", "history-derived.json", bundle.HistoryDerivedJson));
        }

        if (!string.IsNullOrWhiteSpace(bundle.MaterialsDerivedJson))
        {
            entries.Add(new("ontology/hiring-session/testcases-sources", "materials-derived.json", bundle.MaterialsDerivedJson));
        }

        if (!string.IsNullOrWhiteSpace(bundle.TemplateDerivedJson))
        {
            entries.Add(new("ontology/hiring-session/testcases-sources", "template-derived.json", bundle.TemplateDerivedJson));
        }

        return entries;
    }

    private static PackagingTestCasesBundle EnsureBundleMetadata(PackagingTestCasesBundle bundle)
    {
        var mergedJson = bundle.MergedJson.Contains("\"source\"", StringComparison.Ordinal)
            ? bundle.MergedJson
            : PackagingTestCasesJsonValidator.AppendPackagingMetadata(bundle.MergedJson, bundle.Source);

        return bundle with { MergedJson = mergedJson };
    }

    private static HiringRuntimeContext ApplyPackagingTestCasesToWorkingPackage(
        HiringRuntimeContext runtimeContext,
        PackagingTestCasesBundle bundle)
    {
        var enrichedFiles = runtimeContext.WorkingTemplatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);

        UpsertPackageFile(enrichedFiles, PackagingTestCasesRelativePath, bundle.MergedJson);
        UpsertPackageFile(enrichedFiles, PackagingTestCasesOntologyCopyPath, bundle.MergedJson);

        if (!string.IsNullOrWhiteSpace(bundle.SourcesIndexJson))
        {
            UpsertPackageFile(enrichedFiles, PackagingTestCasesSourcesIndexPath, bundle.SourcesIndexJson);
        }

        if (!string.IsNullOrWhiteSpace(bundle.HistoryDerivedJson))
        {
            UpsertPackageFile(enrichedFiles, PackagingTestCasesHistoryDerivedPath, bundle.HistoryDerivedJson);
        }

        if (!string.IsNullOrWhiteSpace(bundle.MaterialsDerivedJson))
        {
            UpsertPackageFile(enrichedFiles, PackagingTestCasesMaterialsDerivedPath, bundle.MaterialsDerivedJson);
        }

        if (!string.IsNullOrWhiteSpace(bundle.TemplateDerivedJson))
        {
            UpsertPackageFile(enrichedFiles, PackagingTestCasesTemplateDerivedPath, bundle.TemplateDerivedJson);
        }

        return runtimeContext with
        {
            WorkingTemplatePackage = runtimeContext.WorkingTemplatePackage with
            {
                PackageFiles = enrichedFiles.Values.ToArray()
            }
        };
    }

    private static readonly string[] PackagingTestCasesSupplementPaths =
    [
        PackagingTestCasesSourcesIndexPath,
        PackagingTestCasesHistoryDerivedPath,
        PackagingTestCasesMaterialsDerivedPath,
        PackagingTestCasesTemplateDerivedPath
    ];

    /// <summary>
    /// import 合并后将 staging（WTP / intermediate）中的 testcase 写入 final，并与 merged 已有兜底 JSON 合并。
    /// </summary>
    private async Task<Dictionary<string, byte[]>> EnrichMergedArtifactsWithPackagingTestCasesAsync(
        IReadOnlyDictionary<string, byte[]> mergedArtifacts,
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        var enriched = mergedArtifacts is Dictionary<string, byte[]> mutable
            ? mutable
            : new Dictionary<string, byte[]>(mergedArtifacts, StringComparer.OrdinalIgnoreCase);

        var stagedSources = await ResolvePackagingTestCaseSourcesAsync(runtimeContext, cancellationToken);
        if (stagedSources.Count == 0)
        {
            return enriched;
        }

        EnrichPrimaryTestCasesJson(enriched, stagedSources);
        EnrichSupplementPaths(enriched, stagedSources);

        return enriched;
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> ResolvePackagingTestCaseSourcesAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        var fromWorkingPackage = BuildPackageFileMap(runtimeContext.WorkingTemplatePackage);
        if (fromWorkingPackage.ContainsKey(PackagingTestCasesRelativePath))
        {
            return fromWorkingPackage;
        }

        if (!runtimeContext.PackagingTestCasesStaged ||
            string.IsNullOrWhiteSpace(runtimeContext.HireId))
        {
            return fromWorkingPackage;
        }

        var intermediateSnapshot = await artifactPackageService.GetPackageByKindAsync(
            runtimeContext.HireId,
            HiringArtifactPackageKinds.IntermediatePackageZip,
            cancellationToken);
        if (intermediateSnapshot is null || intermediateSnapshot.Content.Length == 0)
        {
            return fromWorkingPackage;
        }

        return ExtractZipEntries(intermediateSnapshot.Content);
    }

    private static void EnrichPrimaryTestCasesJson(
        IDictionary<string, byte[]> mergedArtifacts,
        IReadOnlyDictionary<string, byte[]> stagedSources)
    {
        if (!stagedSources.TryGetValue(PackagingTestCasesRelativePath, out var stagedBytes) ||
            stagedBytes.Length == 0)
        {
            return;
        }

        var stagedJson = Encoding.UTF8.GetString(stagedBytes);
        foreach (var path in new[] { PackagingTestCasesRelativePath, PackagingTestCasesOntologyCopyPath })
        {
            if (mergedArtifacts.TryGetValue(path, out var existingBytes) && existingBytes.Length > 0)
            {
                var existingJson = Encoding.UTF8.GetString(existingBytes);
                if (PackagingTestCasesJsonMerger.TryMergeEvaluationTestCasesJson(existingJson, stagedJson, out var mergedJson))
                {
                    mergedArtifacts[path] = Encoding.UTF8.GetBytes(mergedJson);
                }

                continue;
            }

            mergedArtifacts[path] = stagedBytes;
        }
    }

    private static void EnrichSupplementPaths(
        IDictionary<string, byte[]> mergedArtifacts,
        IReadOnlyDictionary<string, byte[]> stagedSources)
    {
        foreach (var path in PackagingTestCasesSupplementPaths)
        {
            if (!stagedSources.TryGetValue(path, out var stagedBytes) || stagedBytes.Length == 0)
            {
                continue;
            }

            mergedArtifacts.TryAdd(path, stagedBytes);
        }
    }

    private sealed record PackagingUploadEntry(string TargetDir, string FileName, string Json);
}
