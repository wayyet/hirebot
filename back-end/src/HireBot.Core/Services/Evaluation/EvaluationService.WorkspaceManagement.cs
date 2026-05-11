using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService
{
    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureWorkspaceReadyAsync(
        string owner,
        EmployeeDetailDto employee,
        string? skillRootPath,
        string? comment,
        bool allowTargetHireCreation,
        bool forceTargetHireRecreate,
        CancellationToken cancellationToken)
    {
        var workspaceKey = BuildWorkspaceKey(owner, employee.EmployeeId);
        var employeeId = employee.EmployeeId;

        // Reuse cached workspace if not forced to recreate
        if (!forceTargetHireRecreate &&
            EvaluationWorkspaces.TryGetValue(workspaceKey, out var cachedWorkspace) &&
            cachedWorkspace.SkillLoadedAtUtc is not null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(cachedWorkspace);
        }

        // Create target sandbox directly via native sandbox API
        var targetResult = await CreateEvaluationSandboxAsync(owner, employeeId, "evaluation-target", cancellationToken);
        if (!targetResult.Success || targetResult.Data.SandboxId is null)
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(targetResult.Code, targetResult.Message);

        var (targetRuntimeId, targetSandboxId) = targetResult.Data;

        // Create evaluator sandbox directly via native sandbox API
        var evaluatorResult = await CreateEvaluationSandboxAsync(owner, employeeId, "evaluation-evaluator", cancellationToken);
        if (!evaluatorResult.Success || evaluatorResult.Data.SandboxId is null)
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(evaluatorResult.Code, evaluatorResult.Message);

        var (evaluatorRuntimeId, evaluatorSandboxId) = evaluatorResult.Data;

        TargetHireBindings[workspaceKey] = targetRuntimeId;

        var workspaceContext = new EvaluationWorkspaceContext(
            TargetHireId: targetRuntimeId,
            TargetSandboxId: targetSandboxId,
            EvaluatorHireId: evaluatorRuntimeId,
            EvaluatorSandboxId: evaluatorSandboxId,
            SkillLoadedAtUtc: null,
            SessionId: null);

        // Upload evaluation-expert skill to evaluator sandbox
        var uploadResult = await UploadSkillToSandboxAsync(evaluatorSandboxId, owner, cancellationToken);
        if (!uploadResult.Success)
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(uploadResult.Code, uploadResult.Message);

        workspaceContext = workspaceContext with { SkillLoadedAtUtc = DateTimeOffset.UtcNow };
        EvaluationWorkspaces[workspaceKey] = workspaceContext;

        logger.LogInformation("[Eval] Workspace ready employeeId={EmployeeId} target={TargetRuntime} evaluator={EvalRuntime}",
            employeeId, targetRuntimeId, evaluatorRuntimeId);

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext);
    }

    private async Task<ApiResponse<(string RuntimeId, string SandboxId)>> CreateEvaluationSandboxAsync(
        string owner,
        string employeeId,
        string sandboxRole,
        CancellationToken cancellationToken)
    {
        var runtimeId = $"eval-{sandboxRole}-{Guid.NewGuid():N}"[..Math.Min(40, 15 + sandboxRole.Length + 32)];
        var createResult = await sandboxService.CreateAsync(
            new SandboxCreateRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = runtimeId,
                SandboxRole = sandboxRole,
                OwnerSubject = owner,
                TenantId = "tenant-default",
                OperatorId = "operator-default",
                ProvisioningMode = "managed",
                UseCase = $"evaluation-{sandboxRole}-for:{employeeId}"
            },
            cancellationToken);

        if (!createResult.Success || createResult.Data is null)
            return ApiResponse<(string, string)>.ErrorResponse(createResult.Code, createResult.Message);

        var sandboxId = createResult.Data.SandboxId;
        logger.LogInformation("[Eval] Creating sandbox runtimeId={RuntimeId} sandboxId={SandboxId} role={Role}",
            runtimeId, sandboxId, sandboxRole);

        for (var i = 0; i < 36; i++)
        {
            await Task.Delay(5000, cancellationToken);
            var refresh = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto { SandboxId = sandboxId, OwnerSubject = owner },
                cancellationToken);
            if (refresh.Success && refresh.Data is not null &&
                string.Equals(refresh.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refresh.Data.GatewayEndpoint))
            {
                logger.LogInformation("[Eval] Sandbox ready runtimeId={RuntimeId} sandboxId={SandboxId}", runtimeId, sandboxId);
                return ApiResponse<(string, string)>.SuccessResponse((runtimeId, sandboxId));
            }
        }

        return ApiResponse<(string, string)>.ErrorResponse(504, $"sandbox {sandboxRole} not ready within 180s");
    }

    private async Task<ApiResponse<bool>> UploadSkillToSandboxAsync(
        string sandboxId,
        string owner,
        CancellationToken cancellationToken)
    {
        const string skillId = "evaluation-expert";
        SystemSkillPackage package;
        try
        {
            package = await systemSkillRegistry.LoadRequiredAsync(skillId, null, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<bool>.ErrorResponse(422, ex.Message);
        }

        if (package.Files.Count == 0)
            return ApiResponse<bool>.ErrorResponse(422, "evaluation skill payload is empty");

        var archiveBytes = BuildSkillArchive(package);
        var uploadResult = await sandboxService.UploadSkillPackageAsync(
            new SkillPackageUploadRequestDto
            {
                SandboxId = sandboxId,
                OwnerSubject = owner,
                ArchiveBytes = archiveBytes,
                FileName = $"{skillId}-{package.Version}.zip"
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
            return ApiResponse<bool>.ErrorResponse(uploadResult.Code, uploadResult.Message);

        logger.LogInformation("[Eval] Skill uploaded sandboxId={SandboxId} installed={Count}",
            sandboxId, uploadResult.Data.SkillsInstalled);
        return ApiResponse<bool>.SuccessResponse(true, "evaluation skill uploaded");
    }

    private static byte[] BuildSkillArchive(SystemSkillPackage package)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in package.Files)
            {
                if (string.IsNullOrWhiteSpace(file.RelativePath)) continue;
                var path = "skills/" + package.SkillId.Trim().Trim('/') + "/" +
                           file.RelativePath.TrimStart('/', '\\').Replace('\\', '/');
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                var contentBytes = System.Text.Encoding.UTF8.GetBytes(file.Content);
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }
        }
        return stream.ToArray();
    }

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureEvaluatorConversationStartedAsync(
        string owner,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(workspaceContext.SessionId))
        {
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext);
        }

        var startResult = await EnsureSandboxConversationStartedAsync(
            owner,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!startResult.Success || startResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(
                startResult.Code,
                $"failed to start evaluator conversation: {startResult.Message}");
        }

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
        {
            SessionId = startResult.Data.SessionId
        });
    }

    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureSupplementConversationPreparedAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var sessionEntity = await GetOrCreateSessionEntityAsync(owner, employee, workspaceContext, cancellationToken);

        var testcaseReady = await dbContext.EvaluationAssets
            .AsNoTracking()
            .AnyAsync(
                item => item.SessionEntityId == sessionEntity.Id && item.AssetType == "testcases-json",
                cancellationToken);
        var ontologyReady = await dbContext.EvaluationAssets
            .AsNoTracking()
            .AnyAsync(
                item => item.SessionEntityId == sessionEntity.Id && item.AssetType == "ontology-json",
                cancellationToken);
        if (testcaseReady && ontologyReady)
        {
            var readyTimelineResult = await GetSandboxTimelineAsync(
                owner,
                workspaceContext.EvaluatorHireId,
                workspaceContext.EvaluatorSandboxId,
                "evaluation-evaluator",
                cancellationToken);
            if (!readyTimelineResult.Success || readyTimelineResult.Data is null)
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(readyTimelineResult.Code, readyTimelineResult.Message);
            }

            if (HasEvaluationReadyPrompt(readyTimelineResult.Data.Messages))
            {
                return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
                {
                    SessionId = readyTimelineResult.Data.SessionId
                });
            }

            var testcaseAssetCandidates = await dbContext.EvaluationAssets
                .AsNoTracking()
                .Where(item =>
                    item.SessionEntityId == sessionEntity.Id &&
                    item.AssetType == "testcases-json")
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var testcaseAssets = testcaseAssetCandidates
                .GroupBy(
                    item => string.IsNullOrWhiteSpace(item.RelatedKey) ? item.RelativePath : item.RelatedKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .First())
                .Take(5)
                .ToArray();
            var cards = await BuildQuestionCardsFromAssetsAsync(testcaseAssets, cancellationToken);
            var questionCardsMarkdown = BuildQuestionCardsMarkdown(cards);
            var ontologyRulesMarkdown = BuildOntologyRulesMarkdown();

            var readySendResult = await SendSandboxMessageAsync(
                owner,
                workspaceContext.EvaluatorHireId,
                workspaceContext.EvaluatorSandboxId,
                "evaluation-evaluator",
                new HiringConversationMessageRequestDto
                {
                    Content = "评估资料已就绪。你可以继续对话询问题卡细节、评分标准，或直接开始执行评估。",
                    StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["evaluation_context_ready"] = "true",
                        ["question_cards_markdown"] = questionCardsMarkdown,
                        ["question_cards_announced"] = "false",
                        ["ontology_rules_markdown"] = ontologyRulesMarkdown
                    }
                },
                cancellationToken);
            if (!readySendResult.Success || readySendResult.Data is null)
            {
                return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(readySendResult.Code, readySendResult.Message);
            }

            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
            {
                SessionId = readySendResult.Data.SessionId
            });
        }

        var timelineResult = await GetSandboxTimelineAsync(
            owner,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            cancellationToken);
        if (!timelineResult.Success || timelineResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(timelineResult.Code, timelineResult.Message);
        }

        if (HasMaterialsSupplementPrompt(timelineResult.Data.Messages))
        {
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
            {
                SessionId = timelineResult.Data.SessionId
            });
        }

        var sendResult = await SendSandboxMessageAsync(
            owner,
            workspaceContext.EvaluatorHireId,
            workspaceContext.EvaluatorSandboxId,
            "evaluation-evaluator",
            new HiringConversationMessageRequestDto
            {
                Content = "检测到评估资料不完整，请引导用户补充缺失素材（测试用例/评估本体），补充后继续执行评估流程。",
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["missing_materials"] = BuildMissingMaterialsSummary(testcaseReady, ontologyReady),
                    ["next_step"] = "请用户上传场景素材或回复场景描述，然后执行 scenario_parser 并重试评估。"
                }
            },
            cancellationToken);
        if (!sendResult.Success || sendResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(sendResult.Code, sendResult.Message);
        }

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext with
        {
            SessionId = sendResult.Data.SessionId
        });
    }

    private async Task<EvaluationReadinessDto> PrimeReadinessMaterialsAsync(
        string employeeId,
        CancellationToken cancellationToken)
    {
        var testcaseResult = await FetchTestcasesAsync(employeeId, cancellationToken);
        if (!testcaseResult.Success)
        {
            logger.LogInformation(
                "Testcase priming failed in LOAD_SKILL. EmployeeId={EmployeeId}, Code={Code}, Message={Message}",
                employeeId,
                testcaseResult.Code,
                testcaseResult.Message);
        }

        var ontologyResult = await QueryOntologyAsync(employeeId, cancellationToken);
        if (!ontologyResult.Success)
        {
            logger.LogInformation(
                "Ontology priming failed in LOAD_SKILL. EmployeeId={EmployeeId}, Code={Code}, Message={Message}",
                employeeId,
                ontologyResult.Code,
                ontologyResult.Message);
        }

        var testcaseReady = testcaseResult.Success &&
            testcaseResult.Data is not null &&
            testcaseResult.Data.Testcases.Count > 0;
        var ontologyReady = ontologyResult.Success &&
            ontologyResult.Data is not null &&
            (ontologyResult.Data.DimensionRules.Count > 0 || ontologyResult.Data.DimensionWeights.Count > 0);

        return BuildReadiness(testcaseReady, ontologyReady);
    }

    private static object BuildEvaluatorPayload(
        string sessionId,
        EvaluationFetchTestcasesResultDto testcaseData,
        EvaluationOntologyQueryResultDto ontologyData,
        IReadOnlyList<TraceExecutionEvidence> executionEvidences)
    {
        return new
        {
            session_id = sessionId,
            target_hire_id = testcaseData.TargetHireId,
            testcase_count = testcaseData.Testcases.Count,
            question_cards = testcaseData.QuestionCards.Select(card => new
            {
                testcase_id = card.TestcaseId,
                title = card.Title,
                prompt = card.Prompt,
                scoring_hint = card.ScoringHint,
                steps = card.Steps
            }),
            ontology = new
            {
                dimension_weights = ontologyData.DimensionWeights,
                dimension_rules = ontologyData.DimensionRules
            },
            executions = executionEvidences.Select(item => new
            {
                testcase_id = item.TestcaseId,
                scenario_name = item.ScenarioName,
                input = item.Input,
                execution_id = item.ExecutionId,
                trace_json = item.TraceJson,
                trace_asset_url = item.TraceAssetUrl
            })
        };
    }

    private static string StripThinkTags(string content)
    {
        var result = content;
        while (true)
        {
            var start = result.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            var end = result.IndexOf("</think>", start + 7, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;
            result = result[..start] + result[(end + 8)..];
        }
        return result;
    }

    private static EvaluatorVerdictResult? ParseSandboxVerdict(string? assistantContent)
    {
        if (string.IsNullOrWhiteSpace(assistantContent))
        {
            return null;
        }

        var trimmed = StripThinkTags(assistantContent).Trim();
        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("verdict", out var verdictElement) &&
                    verdictElement.ValueKind == JsonValueKind.String)
                {
                    var verdictValue = verdictElement.GetString()?.Trim().ToUpperInvariant();
                    var passed = verdictValue is "PASS" or "PASSED" or "SUCCESS";
                    var failed = verdictValue is "FAIL" or "FAILED" or "REJECT";
                    if (passed || failed)
                    {
                        var summary = doc.RootElement.TryGetProperty("summary", out var summaryElement) &&
                                      summaryElement.ValueKind == JsonValueKind.String
                            ? summaryElement.GetString()
                            : null;

                        if (!doc.RootElement.TryGetProperty("overall_score", out var overallScoreElement) ||
                            overallScoreElement.ValueKind is not JsonValueKind.Number ||
                            !overallScoreElement.TryGetDecimal(out var overallScore))
                        {
                            return null;
                        }

                        if (!doc.RootElement.TryGetProperty("dimension_scores", out var dimensionScoresElement) ||
                            dimensionScoresElement.ValueKind is not JsonValueKind.Array)
                        {
                            return null;
                        }

                        var dimensionScores = new List<EvaluationDimensionScoreDto>();
                        foreach (var scoreElement in dimensionScoresElement.EnumerateArray())
                        {
                            if (scoreElement.ValueKind is not JsonValueKind.Object)
                            {
                                continue;
                            }

                            var dimension = scoreElement.TryGetProperty("dimension", out var dimensionElement) &&
                                            dimensionElement.ValueKind == JsonValueKind.String
                                ? dimensionElement.GetString()?.Trim()
                                : null;
                            var score = scoreElement.TryGetProperty("score", out var scoreValueElement) &&
                                        scoreValueElement.ValueKind is JsonValueKind.Number &&
                                        scoreValueElement.TryGetDecimal(out var decimalScore)
                                ? decimalScore
                                : -1m;
                            var comment = scoreElement.TryGetProperty("comment", out var commentElement) &&
                                          commentElement.ValueKind == JsonValueKind.String
                                ? commentElement.GetString()?.Trim()
                                : null;
                            if (string.IsNullOrWhiteSpace(dimension) ||
                                score < 0m || score > 100m ||
                                string.IsNullOrWhiteSpace(comment))
                            {
                                continue;
                            }

                            var evidenceRefs = new List<string>();
                            if (scoreElement.TryGetProperty("evidence_refs", out var evidenceElement) &&
                                evidenceElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var evidenceItem in evidenceElement.EnumerateArray())
                                {
                                    if (evidenceItem.ValueKind != JsonValueKind.String)
                                    {
                                        continue;
                                    }

                                    var value = evidenceItem.GetString();
                                    if (!string.IsNullOrWhiteSpace(value))
                                    {
                                        evidenceRefs.Add(value.Trim());
                                    }
                                }
                            }

                            if (evidenceRefs.Count == 0)
                            {
                                continue;
                            }

                            dimensionScores.Add(new EvaluationDimensionScoreDto(
                                Dimension: dimension,
                                Score: Math.Round(Math.Clamp(score, 0m, 100m), 2),
                                Comment: comment,
                                EvidenceRefs: evidenceRefs));
                        }

                        var requiredDimensions = new HashSet<string>(
                            ["accuracy", "completeness", "compliance", "communication"],
                            StringComparer.OrdinalIgnoreCase);
                        var coveredDimensions = dimensionScores
                            .Select(item => item.Dimension)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!requiredDimensions.SetEquals(coveredDimensions))
                        {
                            return null;
                        }

                        if (overallScore < 0m || overallScore > 100m)
                        {
                            return null;
                        }

                        var normalizedSummary = string.IsNullOrWhiteSpace(summary)
                            ? (passed ? "评估沙箱判定通过。" : "评估沙箱判定未通过。")
                            : summary.Trim();

                        return new EvaluatorVerdictResult(
                            Passed: passed,
                            Summary: normalizedSummary,
                            OverallScore: Math.Round(Math.Clamp(overallScore, 0m, 100m), 2),
                            DimensionScores: dimensionScores,
                            RawVerdictJson: json);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

}
