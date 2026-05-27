using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    private const string PackagingTestCasesRelativePath = "testcases/evaluation-test-cases.json";
    private const string PackagingTestCasesOntologyCopyPath = "ontology/hiring-session/evaluation-test-cases.json";
    private const string PackagingTestCasesSourceHistoryLlm = "kingcrab-history-llm";
    private const string PackagingTestCasesSourceFallback = "packaging-fallback";

    private static readonly JsonSerializerOptions PackagingTestCasesJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    [GeneratedRegex(
        @"生成(?:实例|产物)?包|开始(?:生成)?打包|产物包|template_package|package_workspace|ready_for_packaging|instance_packaging",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex PackagingIntentRegex();

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

        return PackagingIntentRegex().IsMatch(userMessage);
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
        return new SandboxWorkspaceUploadRequestDto
        {
            ScopeType = SandboxScopeTypes.Hire,
            ScopeKey = runtimeContext.HireId,
            SandboxRole = "hiring",
            OwnerSubject = runtimeContext.OwnerSubject,
            SandboxId = runtimeContext.SandboxId,
            TargetDir = "testcases",
            FileName = "evaluation-test-cases.json",
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
    /// 从 KingCrab Session History 经 LLM 生成 testcase JSON。
    /// </summary>
    internal async Task<(bool Success, string Json)> TryBuildPackagingTestCasesFromHistoryAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeContext.SessionId))
        {
            logger.LogWarning(
                "[Hiring] Packaging testcase generation skipped because SessionId is empty. HireId={HireId}",
                runtimeContext.HireId);
            return (false, string.Empty);
        }

        var sessionDetailResult = await sandboxService.GetSessionDetailAsync(
            BuildPackagingSessionDetailRequest(runtimeContext),
            cancellationToken);
        if (!sessionDetailResult.Success || sessionDetailResult.Data is null)
        {
            logger.LogWarning(
                "[Hiring] Failed to load KingCrab session history for packaging testcases. HireId={HireId}, SessionId={SessionId}, Code={Code}, Message={Message}",
                runtimeContext.HireId,
                runtimeContext.SessionId,
                sessionDetailResult.Code,
                sessionDetailResult.Message);
            return (false, string.Empty);
        }

        if (sessionDetailResult.Data.Messages.Count == 0)
        {
            logger.LogWarning(
                "[Hiring] KingCrab session history is empty for packaging testcases. HireId={HireId}, SessionId={SessionId}",
                runtimeContext.HireId,
                runtimeContext.SessionId);
            return (false, string.Empty);
        }

        var generationRequest = new PackagingTestCaseGenerationRequest(
            runtimeContext.TemplateName,
            runtimeContext.StructuredData,
            sessionDetailResult.Data.Messages);

        var generationResult = await packagingTestCaseLlmGenerator.TryGenerateAsync(generationRequest, cancellationToken);
        if (!generationResult.Success)
        {
            return (false, string.Empty);
        }

        var testCasesJson = PackagingTestCaseLlmGenerator.AppendPackagingMetadata(
            generationResult.Json,
            PackagingTestCasesSourceHistoryLlm);

        logger.LogInformation(
            "[Hiring] Generated packaging testcases from KingCrab history via LLM. HireId={HireId}, SessionId={SessionId}, MessageCount={MessageCount}",
            runtimeContext.HireId,
            runtimeContext.SessionId,
            sessionDetailResult.Data.Messages.Count);

        return (true, testCasesJson);
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

        var (historySuccess, historyJson) = await TryBuildPackagingTestCasesFromHistoryAsync(runtimeContext, cancellationToken);
        var testCasesJson = historySuccess ? historyJson : string.Empty;
        if (!historySuccess)
        {
            if (!TryBuildPackagingTestCasesPlaceholder(out testCasesJson))
            {
                return runtimeContext;
            }

            logger.LogWarning(
                "[Hiring] Packaging testcase generation fell back to empty demo structure. HireId={HireId}, SessionId={SessionId}",
                runtimeContext.HireId,
                runtimeContext.SessionId);
        }

        var contentBytes = Encoding.UTF8.GetBytes(testCasesJson);
        var uploadRequest = BuildPackagingTestCaseUploadRequest(runtimeContext, contentBytes);
        var uploadResult = await sandboxService.UploadWorkspaceFileAsync(uploadRequest, cancellationToken);
        if (!uploadResult.Success)
        {
            logger.LogWarning(
                "[Hiring] Failed to stage packaging testcases to sandbox. HireId={HireId}, SandboxId={SandboxId}, Code={Code}, Message={Message}",
                runtimeContext.HireId,
                runtimeContext.SandboxId,
                uploadResult.Code,
                uploadResult.Message);
            return runtimeContext;
        }

        runtimeContext = ApplyPackagingTestCasesToWorkingPackage(runtimeContext, testCasesJson);
        runtimeContext = runtimeContext with { PackagingTestCasesStaged = true };

        logger.LogInformation(
            "[Hiring] Packaging testcases staged to sandbox before package_workspace. HireId={HireId}, SandboxId={SandboxId}, Path={Path}",
            runtimeContext.HireId,
            runtimeContext.SandboxId,
            PackagingTestCasesRelativePath);

        return runtimeContext;
    }

    private static HiringRuntimeContext ApplyPackagingTestCasesToWorkingPackage(
        HiringRuntimeContext runtimeContext,
        string testCasesJson)
    {
        var enrichedFiles = runtimeContext.WorkingTemplatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);

        UpsertPackageFile(enrichedFiles, PackagingTestCasesRelativePath, testCasesJson);
        UpsertPackageFile(enrichedFiles, PackagingTestCasesOntologyCopyPath, testCasesJson);

        return runtimeContext with
        {
            WorkingTemplatePackage = runtimeContext.WorkingTemplatePackage with
            {
                PackageFiles = enrichedFiles.Values.ToArray()
            }
        };
    }
}
