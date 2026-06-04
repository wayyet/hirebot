using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService
{
    private async Task<ApiResponse<EvaluationWorkspaceContext>> EnsureWorkspaceReadyAsync(
        string owner,
        EmployeeDetailDto employee,
        string? skillRootPath,
        bool forceTargetHireRecreate,
        CancellationToken cancellationToken)
    {
        var employeeId = employee.EmployeeId;
        var persistenceScope = ResolveEvaluationPersistenceScope(employee, owner);
        var cachedWorkspace = await LoadWorkspaceContextAsync(persistenceScope, employee.EmployeeId, cancellationToken);

        if (!forceTargetHireRecreate &&
            cachedWorkspace is not null &&
            cachedWorkspace.SkillLoadedAtUtc is not null &&
            !string.IsNullOrWhiteSpace(cachedWorkspace.TargetSandboxId) &&
            !string.IsNullOrWhiteSpace(cachedWorkspace.EvaluatorSandboxId) &&
            !string.IsNullOrWhiteSpace(cachedWorkspace.TargetHireId) &&
            !string.IsNullOrWhiteSpace(cachedWorkspace.EvaluatorHireId))
        {
            // 验证沙箱容器是否仍然存活；若已被删除，RefreshAsync 会以相同 ScopeKey 重建容器（PVC 数据保留）
            var targetRefresh = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = cachedWorkspace.TargetSandboxId,
                    OwnerSubject = owner,
                    ScopeType = SandboxScopeTypes.Managed,
                    ScopeKey = cachedWorkspace.TargetHireId,
                    SandboxRole = "evaluation-target"
                },
                cancellationToken);
            var evaluatorRefresh = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = cachedWorkspace.EvaluatorSandboxId,
                    OwnerSubject = owner,
                    ScopeType = SandboxScopeTypes.Managed,
                    ScopeKey = cachedWorkspace.EvaluatorHireId,
                    SandboxRole = "evaluation-evaluator"
                },
                cancellationToken);

            var newTargetSandboxId = targetRefresh.Data?.SandboxId ?? cachedWorkspace.TargetSandboxId;
            var newEvaluatorSandboxId = evaluatorRefresh.Data?.SandboxId ?? cachedWorkspace.EvaluatorSandboxId;
            var sandboxRecreated =
                !string.Equals(newTargetSandboxId, cachedWorkspace.TargetSandboxId, StringComparison.Ordinal) ||
                !string.Equals(newEvaluatorSandboxId, cachedWorkspace.EvaluatorSandboxId, StringComparison.Ordinal);

            if (!sandboxRecreated)
            {
                logger.LogInformation(
                    "[Eval] Reusing cached workspace employeeId={EmployeeId} targetSandboxId={TargetSandboxId} evaluatorSandboxId={EvaluatorSandboxId}",
                    employeeId,
                    cachedWorkspace.TargetSandboxId,
                    cachedWorkspace.EvaluatorSandboxId);
                return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(cachedWorkspace);
            }

            // 沙箱已被删除并以相同 ScopeKey 重建（PVC 中的历史数据得以保留），需重新上传 skill/模板/素材
            logger.LogInformation(
                "[Eval] Sandboxes were recreated after deletion; restoring workspace. employeeId={EmployeeId} newTarget={NewTarget} newEvaluator={NewEval}",
                employeeId, newTargetSandboxId, newEvaluatorSandboxId);
            return await RestoreWorkspaceAfterRecreationAsync(
                owner, employee, skillRootPath, persistenceScope,
                cachedWorkspace, newTargetSandboxId, newEvaluatorSandboxId, cancellationToken);
        }

        var stepStates = new Dictionary<string, WorkspaceStepState>(StringComparer.OrdinalIgnoreCase)
        {
            ["target_sandbox"] = new("running", null)
        };

        var targetResult = await CreateEvaluationSandboxAsync(owner, employeeId, "evaluation-target", useStableRuntimeId: false, cancellationToken);
        if (!targetResult.Success || targetResult.Data.SandboxId is null)
        {
            stepStates["target_sandbox"] = new("failed", targetResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(targetResult.Code, targetResult.Message);
        }

        var (targetRuntimeId, targetSandboxId) = targetResult.Data;
        logger.LogInformation(
            "[Eval] Target sandbox ready employeeId={EmployeeId} runtimeId={RuntimeId} sandboxId={SandboxId}",
            employeeId,
            targetRuntimeId,
            targetSandboxId);
        stepStates["target_sandbox"] = new("completed", targetSandboxId);
        stepStates["upload_target_template"] = new("running", null);
        var targetTemplateResult = await UploadHiringTemplateToTargetSandboxAsync(targetSandboxId, owner, employee, cancellationToken);
        if (!targetTemplateResult.Success)
        {
            stepStates["upload_target_template"] = new("failed", targetTemplateResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(targetTemplateResult.Code, targetTemplateResult.Message);
        }
        stepStates["upload_target_template"] = new("completed", null);
        stepStates["evaluator_sandbox"] = new("running", null);

        var evaluatorResult = await CreateEvaluationSandboxAsync(
            owner,
            employeeId,
            "evaluation-evaluator",
            useStableRuntimeId: false,
            cancellationToken);
        if (!evaluatorResult.Success || evaluatorResult.Data.SandboxId is null)
        {
            stepStates["evaluator_sandbox"] = new("failed", evaluatorResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(evaluatorResult.Code, evaluatorResult.Message);
        }

        var (evaluatorRuntimeId, evaluatorSandboxId) = evaluatorResult.Data;
        logger.LogInformation(
            "[Eval] Evaluator sandbox ready employeeId={EmployeeId} runtimeId={RuntimeId} sandboxId={SandboxId}",
            employeeId,
            evaluatorRuntimeId,
            evaluatorSandboxId);
        stepStates["evaluator_sandbox"] = new("completed", evaluatorSandboxId);

        var workspaceContext = new EvaluationWorkspaceContext(
            TargetHireId: targetRuntimeId,
            TargetSandboxId: targetSandboxId,
            EvaluatorHireId: evaluatorRuntimeId,
            EvaluatorSandboxId: evaluatorSandboxId,
            SkillLoadedAtUtc: null,
            SessionId: null,
            EvaluatorTemplatePackageZipPath: null,
            UploadedTemplatePackageZipPath: null,
            ArtifactWorkspaceDir: null,
            StepStates: stepStates);
        await SaveWorkspaceContextAsync(persistenceScope, employee.EmployeeId, workspaceContext, cancellationToken);

        stepStates["upload_skill"] = new("running", null);
        var uploadResult = await UploadEvaluationTemplateToSandboxAsync(evaluatorSandboxId, owner, cancellationToken);
        if (!uploadResult.Success)
        {
            stepStates["upload_skill"] = new("failed", uploadResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(uploadResult.Code, uploadResult.Message);
        }

        stepStates["upload_skill"] = new("completed", null);

        stepStates["upload_employee_template"] = new("running", null);
        // TODO: 暂时禁用 hiring template 到 evaluator 沙箱的安装上传，
        // 避免覆盖已安装的 evaluation-expert 技能（两次 UploadDigitalEmployeeTemplateAsync 到同一沙箱，后者会替换前者）。
        // 待确认沙箱 /admin/digital-employee/upload 接口支持多技能共存，或改为 workspace 文件上传后再启用。
        // if (targetTemplateResult.Data is { } hiringArchive)
        // {
        //     var evaluatorTemplateResult = await UploadHiringTemplateToEvaluatorSandboxAsync(
        //         evaluatorSandboxId, owner, hiringArchive, cancellationToken);
        //     if (!evaluatorTemplateResult.Success)
        //     {
        //         stepStates["upload_employee_template"] = new("failed", evaluatorTemplateResult.Message);
        //         return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(evaluatorTemplateResult.Code, evaluatorTemplateResult.Message);
        //     }
        // }

        workspaceContext = workspaceContext with
        {
            UploadedTemplatePackageZipPath = targetTemplateResult.Data?.LocalCachePath
        };
        await SaveWorkspaceContextAsync(persistenceScope, employee.EmployeeId, workspaceContext, cancellationToken);
        stepStates["upload_employee_template"] = new("completed", null);

        stepStates["upload_artifacts"] = new("running", null);
        var targetArtifactUploadResult = await UploadArtifactToSandboxAsync(
            targetSandboxId,
            owner,
            employee,
            skillRootPath,
            "target",
            cancellationToken);
        if (!targetArtifactUploadResult.Success)
        {
            logger.LogWarning("[Eval] Target artifact upload skipped sandboxId={SandboxId} Message={Message}",
                targetSandboxId, targetArtifactUploadResult.Message);
        }

        var evaluatorArtifactUploadResult = await UploadArtifactAttachmentToSandboxAsync(
            evaluatorSandboxId,
            evaluatorRuntimeId,
            owner,
            employee,
            skillRootPath,
            "evaluator",
            cancellationToken);
        if (!evaluatorArtifactUploadResult.Success)
        {
            logger.LogWarning("[Eval] Evaluator artifact upload skipped sandboxId={SandboxId} Message={Message}",
                evaluatorSandboxId, evaluatorArtifactUploadResult.Message);
        }

        stepStates["upload_artifacts"] = new("completed", null);

        stepStates["upload_materials"] = new("running", null);
        var materialFiles = targetTemplateResult.Data?.MaterialFiles ?? [];
        var materialUploadResult = await UploadMaterialFilesToEvaluatorAsync(
            evaluatorSandboxId,
            evaluatorRuntimeId,
            owner,
            materialFiles,
            cancellationToken);
        if (!materialUploadResult.Success)
        {
            logger.LogWarning("[Eval] Material upload failed sandboxId={SandboxId} Message={Message}",
                evaluatorSandboxId, materialUploadResult.Message);
        }
        stepStates["upload_materials"] = new("completed", $"{materialFiles.Count} files");

        workspaceContext = workspaceContext with
        {
            SkillLoadedAtUtc = DateTimeOffset.UtcNow,
            ArtifactWorkspaceDir = evaluatorArtifactUploadResult.Data,
            TestcaseOutlines = materialUploadResult.Data ?? []
        };
        await SaveWorkspaceContextAsync(persistenceScope, employee.EmployeeId, workspaceContext, cancellationToken);

        logger.LogInformation("[Eval] Workspace ready employeeId={EmployeeId} target={TargetRuntime} evaluator={EvalRuntime}",
            employeeId, targetRuntimeId, evaluatorRuntimeId);

        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(workspaceContext);
    }

    private async Task<ApiResponse<(string RuntimeId, string SandboxId)>> CreateEvaluationSandboxAsync(
        string owner,
        string employeeId,
        string sandboxRole,
        bool useStableRuntimeId,
        CancellationToken cancellationToken)
    {
        var runtimeId = useStableRuntimeId
            ? BuildEvaluationRuntimeId(employeeId, sandboxRole)
            : $"eval-{sandboxRole}-{Guid.NewGuid():N}"[..Math.Min(40, 15 + sandboxRole.Length + 32)];

        if (useStableRuntimeId)
        {
            var existing = await dbContext.SandboxInstances
                .AsNoTracking()
                .Where(item =>
                    item.OwnerSubject == owner &&
                    item.ScopeType == SandboxScopeTypes.Managed &&
                    item.ScopeKey == runtimeId &&
                    item.SandboxRole == sandboxRole &&
                    item.State != "Deleted")
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                var existingReady = await WaitForEvaluationSandboxReadyAsync(
                    owner,
                    runtimeId,
                    existing.SandboxId,
                    sandboxRole,
                    cancellationToken);
                if (existingReady.Success)
                {
                    return existingReady;
                }
            }
        }

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
                UseCase = $"evaluation-{sandboxRole}-for:{employeeId}",
                Metadata = new Dictionary<string, string>
                {
                    [SandboxMetaKeys.UserSubject] = owner,
                    [SandboxMetaKeys.EmployeeId] = employeeId,
                    [SandboxMetaKeys.EvalScopeKey] = runtimeId
                }
            },
            cancellationToken);

        if (!createResult.Success || createResult.Data is null)
            return ApiResponse<(string, string)>.ErrorResponse(createResult.Code, createResult.Message);

        var sandboxId = createResult.Data.SandboxId;
        logger.LogInformation("[Eval] Creating sandbox runtimeId={RuntimeId} sandboxId={SandboxId} role={Role}",
            runtimeId, sandboxId, sandboxRole);

        return await WaitForEvaluationSandboxReadyAsync(
            owner,
            runtimeId,
            sandboxId,
            sandboxRole,
            cancellationToken);
    }

    private async Task<ApiResponse<(string RuntimeId, string SandboxId)>> WaitForEvaluationSandboxReadyAsync(
        string owner,
        string runtimeId,
        string sandboxId,
        string sandboxRole,
        CancellationToken cancellationToken)
    {
        // 先检查是否已就绪，避免首次轮询前不必要的等待（与雇佣侧保持一致）
        var initial = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto { SandboxId = sandboxId, OwnerSubject = owner },
            cancellationToken);
        if (initial.Success && initial.Data is not null &&
            string.Equals(initial.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(initial.Data.GatewayEndpoint))
        {
            logger.LogInformation("[Eval] Sandbox already ready runtimeId={RuntimeId} sandboxId={SandboxId}", runtimeId, sandboxId);
            return ApiResponse<(string, string)>.SuccessResponse((runtimeId, sandboxId));
        }

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

    /// <summary>
    /// 沙箱容器被删除后，以相同 ScopeKey（PVC 不变）重建完成，重新上传 skill/模板/素材，恢复可用状态。
    /// </summary>
    private async Task<ApiResponse<EvaluationWorkspaceContext>> RestoreWorkspaceAfterRecreationAsync(
        string owner,
        EmployeeDetailDto employee,
        string? skillRootPath,
        string persistenceScope,
        EvaluationWorkspaceContext previousContext,
        string newTargetSandboxId,
        string newEvaluatorSandboxId,
        CancellationToken cancellationToken)
    {
        var employeeId = employee.EmployeeId;

        // 等待两个新容器就绪
        var targetReady = await WaitForEvaluationSandboxReadyAsync(
            owner, previousContext.TargetHireId, newTargetSandboxId, "evaluation-target", cancellationToken);
        if (!targetReady.Success)
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(targetReady.Code, targetReady.Message);

        var evaluatorReady = await WaitForEvaluationSandboxReadyAsync(
            owner, previousContext.EvaluatorHireId, newEvaluatorSandboxId, "evaluation-evaluator", cancellationToken);
        if (!evaluatorReady.Success)
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(evaluatorReady.Code, evaluatorReady.Message);

        var stepStates = previousContext.StepStates is not null
            ? new Dictionary<string, WorkspaceStepState>(previousContext.StepStates, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WorkspaceStepState>(StringComparer.OrdinalIgnoreCase);

        // 先以新 ID 覆盖缓存（SkillLoadedAtUtc=null），防止途中失败时缓存仍指向已删除的沙箱
        var restoringContext = previousContext with
        {
            TargetSandboxId = newTargetSandboxId,
            EvaluatorSandboxId = newEvaluatorSandboxId,
            SkillLoadedAtUtc = null,
            StepStates = stepStates
        };
        await SaveWorkspaceContextAsync(persistenceScope, employeeId, restoringContext, cancellationToken);

        // 重新上传雇佣模板到 target 沙箱
        stepStates["restore_target_template"] = new("running", null);
        var targetTemplateResult = await UploadHiringTemplateToTargetSandboxAsync(
            newTargetSandboxId, owner, employee, cancellationToken);
        if (!targetTemplateResult.Success)
        {
            stepStates["restore_target_template"] = new("failed", targetTemplateResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(targetTemplateResult.Code, targetTemplateResult.Message);
        }
        stepStates["restore_target_template"] = new("completed", null);

        // 重新上传评估 skill 模板到 evaluator 沙箱
        stepStates["restore_eval_skill"] = new("running", null);
        var evalSkillResult = await UploadEvaluationTemplateToSandboxAsync(
            newEvaluatorSandboxId, owner, cancellationToken);
        if (!evalSkillResult.Success)
        {
            stepStates["restore_eval_skill"] = new("failed", evalSkillResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(evalSkillResult.Code, evalSkillResult.Message);
        }
        stepStates["restore_eval_skill"] = new("completed", null);

        // 重新上传 artifact（失败不阻断主流程）
        stepStates["restore_artifacts"] = new("running", null);
        var targetArtifactResult = await UploadArtifactToSandboxAsync(
            newTargetSandboxId, owner, employee, skillRootPath, "target", cancellationToken);
        if (!targetArtifactResult.Success)
            logger.LogWarning("[Eval] Target artifact restore skipped sandboxId={SandboxId} msg={Msg}",
                newTargetSandboxId, targetArtifactResult.Message);

        var evaluatorArtifactResult = await UploadArtifactAttachmentToSandboxAsync(
            newEvaluatorSandboxId, previousContext.EvaluatorHireId, owner, employee, skillRootPath, "evaluator", cancellationToken);
        if (!evaluatorArtifactResult.Success)
            logger.LogWarning("[Eval] Evaluator artifact restore skipped sandboxId={SandboxId} msg={Msg}",
                newEvaluatorSandboxId, evaluatorArtifactResult.Message);
        stepStates["restore_artifacts"] = new("completed", null);

        // 重新上传评测素材（失败不阻断）
        stepStates["restore_materials"] = new("running", null);
        var materialFiles = targetTemplateResult.Data?.MaterialFiles ?? [];
        var materialResult = await UploadMaterialFilesToEvaluatorAsync(
            newEvaluatorSandboxId, previousContext.EvaluatorHireId, owner, materialFiles, cancellationToken);
        if (!materialResult.Success)
            logger.LogWarning("[Eval] Materials restore skipped sandboxId={SandboxId} msg={Msg}",
                newEvaluatorSandboxId, materialResult.Message);
        stepStates["restore_materials"] = new("completed", $"{materialFiles.Count} files");

        var restoredContext = restoringContext with
        {
            SkillLoadedAtUtc = DateTimeOffset.UtcNow,
            UploadedTemplatePackageZipPath = targetTemplateResult.Data?.LocalCachePath,
            ArtifactWorkspaceDir = evaluatorArtifactResult.Success
                ? evaluatorArtifactResult.Data
                : previousContext.ArtifactWorkspaceDir,
            TestcaseOutlines = materialResult.Data ?? previousContext.TestcaseOutlines ?? [],
            StepStates = stepStates
        };
        await SaveWorkspaceContextAsync(persistenceScope, employeeId, restoredContext, cancellationToken);

        logger.LogInformation(
            "[Eval] Workspace restored after sandbox recreation. employeeId={EmployeeId} targetSandboxId={TargetSandboxId} evaluatorSandboxId={EvaluatorSandboxId}",
            employeeId, newTargetSandboxId, newEvaluatorSandboxId);
        return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(restoredContext);
    }

    private static string BuildEvaluationRuntimeId(string employeeId, string sandboxRole)
    {
        var raw = $"eval-{sandboxRole}-{employeeId}".Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(c, '_');
        }

        return raw.Length <= 100 ? raw : raw[..100];
    }

    private async Task<ApiResponse<bool>> UploadEvaluationTemplateToSandboxAsync(
        string sandboxId,
        string owner,
        CancellationToken cancellationToken)
    {
        const string templatePackageId = "evaluation-expert";
        var templateRoot = evaluationTemplatePackageRoot;
        if (string.IsNullOrWhiteSpace(templateRoot) || !Directory.Exists(templateRoot))
        {
            return ApiResponse<bool>.ErrorResponse(404, $"evaluation template package root not found: {templateRoot}");
        }

        TemplatePackageDefinition templatePackage;
        try
        {
            templatePackage = await fileSystemTemplatePackageProvider.LoadFromDirectoryAsync(
                templateRoot, templatePackageId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Eval] Failed to load evaluation template package from {TemplateRoot}", templateRoot);
            return ApiResponse<bool>.ErrorResponse(422, $"failed to load evaluation template package: {ex.Message}");
        }

        if (templatePackage.PackageFiles.Count == 0)
        {
            return ApiResponse<bool>.ErrorResponse(422, "evaluation template package has no files");
        }

        var archiveBytes = TemplatePackageArchiveBuilder.BuildArchive(templatePackage);
        if (archiveBytes.Length == 0)
        {
            return ApiResponse<bool>.ErrorResponse(422, "evaluation template package archive is empty");
        }

        var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
        var uploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
            new DigitalEmployeeTemplateUploadRequestDto
            {
                SandboxId = sandboxId,
                OwnerSubject = owner,
                ArchiveBytes = archiveBytes,
                FileName = fileName
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
            return ApiResponse<bool>.ErrorResponse(uploadResult.Code, uploadResult.Message);

        logger.LogInformation("[Eval] Evaluation template package uploaded sandboxId={SandboxId} installed={Count}",
            sandboxId, uploadResult.Data.SkillsInstalled);
        return ApiResponse<bool>.SuccessResponse(true, "evaluation template package uploaded");
    }

    private async Task<ApiResponse<bool>> UploadArtifactToSandboxAsync(
        string sandboxId,
        string owner,
        EmployeeDetailDto employee,
        string? explicitArtifactPath,
        string sandboxSide,
        CancellationToken cancellationToken)
    {
        var bundleResult = await BuildArtifactBundleAsync(employee, explicitArtifactPath, cancellationToken);
        if (!bundleResult.Success || bundleResult.Data is null)
        {
            return ApiResponse<bool>.ErrorResponse(bundleResult.Code, bundleResult.Message);
        }

        var artifactBundle = bundleResult.Data;
        var uploadFromBundleResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
            new DigitalEmployeeTemplateUploadRequestDto
            {
                SandboxId = sandboxId,
                OwnerSubject = owner,
                ArchiveBytes = artifactBundle.Content,
                FileName = artifactBundle.FileName
            },
            cancellationToken);

        if (!uploadFromBundleResult.Success || uploadFromBundleResult.Data is null)
        {
            return ApiResponse<bool>.ErrorResponse(uploadFromBundleResult.Code, uploadFromBundleResult.Message);
        }

        logger.LogInformation(
            "[Eval] {SandboxSide} artifact uploaded sandboxId={SandboxId} fileName={FileName} installed={Count}",
            sandboxSide,
            sandboxId,
            artifactBundle.FileName,
            uploadFromBundleResult.Data.SkillsInstalled);
        return ApiResponse<bool>.SuccessResponse(true, $"{sandboxSide} artifact uploaded");
    }

    private async Task<ApiResponse<string>> UploadArtifactAttachmentToSandboxAsync(
        string sandboxId,
        string scopeKey,
        string owner,
        EmployeeDetailDto employee,
        string? explicitArtifactPath,
        string sandboxSide,
        CancellationToken cancellationToken)
    {
        var bundleResult = await BuildArtifactBundleAsync(employee, explicitArtifactPath, cancellationToken);
        if (!bundleResult.Success || bundleResult.Data is null)
            return ApiResponse<string>.ErrorResponse(bundleResult.Code, bundleResult.Message);

        var bundle = bundleResult.Data;
        // 直接解压到 workspace/uploads/artifact/ 目录，evaluator skill 可通过文件系统路径读取
        var uploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = scopeKey,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                SandboxId = sandboxId,
                TargetDir = "uploads/artifact",
                FileName = bundle.FileName,
                Content = bundle.Content,
                ContentType = "application/zip"
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
            return ApiResponse<string>.ErrorResponse(uploadResult.Code, uploadResult.Message);

        logger.LogInformation(
            "[Eval] {SandboxSide} artifact uploaded to workspace sandboxId={SandboxId} dir={WorkspaceDir} fileName={FileName} fileCount={Count}",
            sandboxSide, sandboxId, uploadResult.Data.WorkspaceDir, bundle.FileName, uploadResult.Data.FileCount);
        return ApiResponse<string>.SuccessResponse(uploadResult.Data.WorkspaceDir, $"{sandboxSide} artifact uploaded to workspace");
    }

    private async Task<ApiResponse<TargetArtifactBundle>> BuildArtifactBundleAsync(
        EmployeeDetailDto employee,
        string? explicitArtifactPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitArtifactPath))
        {
            var normalizedPath = explicitArtifactPath.Trim();
            if (Directory.Exists(normalizedPath))
            {
                var bundle = await ZipDirectoryAsBundleAsync(
                    normalizedPath,
                    $"{Path.GetFileName(normalizedPath)}.zip",
                    sourceType: "explicit-directory",
                    cancellationToken);
                return bundle.Content.Length == 0
                    ? ApiResponse<TargetArtifactBundle>.ErrorResponse(422, "artifact archive is empty")
                    : ApiResponse<TargetArtifactBundle>.SuccessResponse(bundle);
            }

            if (File.Exists(normalizedPath) && normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var archiveBytes = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
                if (archiveBytes.Length == 0)
                {
                    return ApiResponse<TargetArtifactBundle>.ErrorResponse(422, "artifact archive is empty");
                }

                return ApiResponse<TargetArtifactBundle>.SuccessResponse(
                    new TargetArtifactBundle(
                        FileName: Path.GetFileName(normalizedPath),
                        Content: archiveBytes,
                        Sha256: Convert.ToHexStringLower(SHA256.HashData(archiveBytes)),
                        SourceType: "explicit-zip",
                        SourcePath: normalizedPath));
            }

            return ApiResponse<TargetArtifactBundle>.ErrorResponse(404, $"explicit artifact path not found: {normalizedPath}");
        }

        var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(employee.EmployeeId, cancellationToken);
        if (packageSnapshot?.Content is { Length: > 0 })
        {
            return ApiResponse<TargetArtifactBundle>.SuccessResponse(
                new TargetArtifactBundle(
                    FileName: string.IsNullOrWhiteSpace(packageSnapshot.FileName)
                        ? $"hiring_artifacts_{employee.EmployeeId}.zip"
                        : packageSnapshot.FileName,
                    Content: packageSnapshot.Content,
                    Sha256: Convert.ToHexStringLower(SHA256.HashData(packageSnapshot.Content)),
                    SourceType: "artifact-package-service",
                    SourcePath: employee.EmployeeId));
        }

        var fixtureDir = ResolveFixtureArtifactDirectory(employee.EmployeeId, employee);
        if (string.IsNullOrWhiteSpace(fixtureDir))
        {
            return ApiResponse<TargetArtifactBundle>.ErrorResponse(404, $"no artifact package or fixture directory found for employee {employee.EmployeeId}");
        }

        var fixtureBundle = await ZipDirectoryAsBundleAsync(
            fixtureDir,
            $"fixture_{employee.EmployeeId}.zip",
            sourceType: "fixture",
            cancellationToken);
        return fixtureBundle.Content.Length == 0
            ? ApiResponse<TargetArtifactBundle>.ErrorResponse(422, "artifact archive is empty")
            : ApiResponse<TargetArtifactBundle>.SuccessResponse(fixtureBundle);
    }

    /// <summary>
    /// 加载雇佣端产生的数字员工模板，并将其上传到评估对象（target）沙箱。
    /// 返回已加载的归档内容，供后续 evaluator workspace 上传复用（避免二次加载）。
    /// </summary>
    private async Task<ApiResponse<HiringTemplateArchive?>> UploadHiringTemplateToTargetSandboxAsync(
        string targetSandboxId,
        string owner,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var templateId = employee.SourceTemplateId?.Trim();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            logger.LogWarning("[Eval] Employee {EmployeeId} has no SourceTemplateId, skipping target template upload", employee.EmployeeId);
            return ApiResponse<HiringTemplateArchive?>.SuccessResponse(null, "target template upload skipped: missing SourceTemplateId");
        }

        TemplatePackageDefinition templatePackage;
        var fixtureBinding = ResolveFixtureTemplateBinding(templateId);
        if (fixtureBinding is not null)
        {
            var fixtureTemplateRoot = ResolveBoundFixtureTemplatePackageRoot(templateId, employee, fixtureBinding);
            if (string.IsNullOrWhiteSpace(fixtureTemplateRoot))
            {
                logger.LogError(
                    "[Eval] Fixture template binding exists but local package root was not found templateId={TemplateId} employeeId={EmployeeId}",
                    templateId,
                    employee.EmployeeId);
                return ApiResponse<HiringTemplateArchive?>.ErrorResponse(
                    404,
                    $"fixture template package not found for templateId: {templateId}");
            }

            try
            {
                templatePackage = await fileSystemTemplatePackageProvider.LoadFromDirectoryAsync(
                    fixtureTemplateRoot,
                    templateId,
                    cancellationToken);
                logger.LogInformation(
                    "[Eval] Loaded template package from fixture binding templateId={TemplateId} packageRoot={PackageRoot}",
                    templateId,
                    fixtureTemplateRoot);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "[Eval] Failed to load bound fixture template package templateId={TemplateId} packageRoot={PackageRoot}",
                    templateId,
                    fixtureTemplateRoot);
                return ApiResponse<HiringTemplateArchive?>.ErrorResponse(
                    422,
                    $"failed to load fixture template package: {templateId}");
            }
        }
        else
        {
            try
            {
                templatePackage = await templatePackageProvider.LoadAsync(templateId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Eval] Failed to load template package templateId={TemplateId}", templateId);
                return ApiResponse<HiringTemplateArchive?>.ErrorResponse(
                    502,
                    $"failed to load template package: {templateId}");
            }
        }

        if (templatePackage.PackageFiles.Count == 0)
        {
            logger.LogWarning("[Eval] Template package {TemplateId} has no files", templateId);
            return ApiResponse<HiringTemplateArchive?>.SuccessResponse(null, "target template upload skipped: package has no files");
        }

        var archiveBytes = TemplatePackageArchiveBuilder.BuildArchive(templatePackage);
        if (archiveBytes.Length == 0)
        {
            logger.LogError("[Eval] Template archive is empty for templateId={TemplateId}", templateId);
            return ApiResponse<HiringTemplateArchive?>.ErrorResponse(422, "employee template archive is empty");
        }

        var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
        var localCachePath = await PersistUploadedTemplatePackageArchiveAsync(fileName, archiveBytes, cancellationToken);

        var uploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
            new DigitalEmployeeTemplateUploadRequestDto
            {
                SandboxId = targetSandboxId,
                OwnerSubject = owner,
                ArchiveBytes = archiveBytes,
                FileName = fileName
            },
            cancellationToken);
        if (!uploadResult.Success || uploadResult.Data is null)
        {
            logger.LogError("[Eval] Failed to upload hiring template to target sandboxId={SandboxId} code={Code} msg={Message}",
                targetSandboxId, uploadResult.Code, uploadResult.Message);
            return ApiResponse<HiringTemplateArchive?>.ErrorResponse(
                uploadResult.Code,
                $"failed to upload hiring template to target sandbox: {uploadResult.Message}");
        }

        logger.LogInformation("[Eval] Hiring template uploaded to target sandboxId={SandboxId} installed={Count}",
            targetSandboxId, uploadResult.Data.SkillsInstalled);

        var materialFiles = ExtractMaterialFiles(templatePackage);
        logger.LogInformation("[Eval] Extracted material files from template package count={Count}", materialFiles.Count);

        return ApiResponse<HiringTemplateArchive?>.SuccessResponse(
            new HiringTemplateArchive(archiveBytes, fileName, localCachePath, materialFiles));
    }

    /// <summary>
    /// 从模板包文件列表中提取 testcase / ontology 材料文件，
    /// 与 Python material_loader.py 的 _matches_material 逻辑保持一致。
    /// </summary>
    private static IReadOnlyList<TemplateMaterialFile> ExtractMaterialFiles(TemplatePackageDefinition templatePackage)
    {
        var results = new List<TemplateMaterialFile>();

        foreach (var file in templatePackage.PackageFiles)
        {
            var normalizedPath = file.RelativePath.Replace('\\', '/').ToLowerInvariant();
            var fileName = Path.GetFileName(file.RelativePath);
            var fileNameLower = fileName.ToLowerInvariant();
            var extension = Path.GetExtension(fileNameLower);

            if (extension == ".json" && (
                normalizedPath.Contains("/testcases/") ||
                normalizedPath.StartsWith("testcases/") ||
                fileNameLower.Contains("testcase") ||
                fileNameLower.Contains("test-case") ||
                fileNameLower.Contains("evaluation-test")))
            {
                results.Add(new TemplateMaterialFile("testcases", fileName, file.Content));
                continue;
            }

            if (extension is ".json" or ".md" or ".txt" &&
                !fileNameLower.Contains("testcase") &&
                !fileNameLower.Contains("test-case") &&
                (normalizedPath.Contains("/ontology/") ||
                 normalizedPath.StartsWith("ontology/") ||
                 fileNameLower.Contains("ontology") ||
                 fileNameLower.Contains("rubric") ||
                 fileNameLower.Contains("evaluation")))
            {
                results.Add(new TemplateMaterialFile("ontology", fileName, file.Content));
            }
        }

        // 从 OntologySlices 补充尚未被 PackageFiles 覆盖的 ontology 内容
        var existingOntologyNames = results
            .Where(f => f.TargetDir == "ontology")
            .Select(f => f.FileName.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var slice in templatePackage.OntologySlices)
        {
            var sliceFileName = Path.GetFileName(slice.RelativePath);
            if (!existingOntologyNames.Contains(sliceFileName.ToLowerInvariant()))
            {
                results.Add(new TemplateMaterialFile(
                    "ontology",
                    sliceFileName,
                    System.Text.Encoding.UTF8.GetBytes(slice.Content)));
            }
        }

        return results;
    }

    /// <summary>
    /// <summary>
    /// 将雇佣端模板归档（由 <see cref="UploadHiringTemplateToTargetSandboxAsync"/> 已加载）
    /// 安装到 evaluator 沙箱，与 target 沙箱使用相同的上传通道，保证模板包格式一致。
    /// </summary>
    private async Task<ApiResponse<bool>> UploadHiringTemplateToEvaluatorSandboxAsync(
        string evaluatorSandboxId,
        string owner,
        HiringTemplateArchive archive,
        CancellationToken cancellationToken)
    {
        var uploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
            new DigitalEmployeeTemplateUploadRequestDto
            {
                SandboxId = evaluatorSandboxId,
                OwnerSubject = owner,
                ArchiveBytes = archive.ArchiveBytes,
                FileName = archive.FileName
            },
            cancellationToken);
        if (!uploadResult.Success || uploadResult.Data is null)
        {
            logger.LogError("[Eval] Failed to upload hiring template to evaluator sandboxId={SandboxId} code={Code} msg={Message}",
                evaluatorSandboxId, uploadResult.Code, uploadResult.Message);
            return ApiResponse<bool>.ErrorResponse(
                uploadResult.Code,
                $"failed to upload hiring template to evaluator sandbox: {uploadResult.Message}");
        }

        logger.LogInformation("[Eval] Hiring template uploaded to evaluator sandboxId={SandboxId} installed={Count}",
            evaluatorSandboxId, uploadResult.Data.SkillsInstalled);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    /// <summary>
    /// 将提取好的 testcase / ontology 文件逐一上传到 evaluator 沙箱的 workspace 目录，
    /// Python material_loader.py 通过扫描 workspace_root 即可发现这些文件。
    /// </summary>
    private async Task<ApiResponse<IReadOnlyList<EvaluationTestcaseOutline>>> UploadMaterialFilesToEvaluatorAsync(
        string evaluatorSandboxId,
        string evaluatorScopeKey,
        string owner,
        IReadOnlyList<TemplateMaterialFile> materialFiles,
        CancellationToken cancellationToken)
    {
        // 没有业务测试用例时保持 test-cases 为空，让评估 skill 进入 STEP 1.5 提示/合成。
        var testcaseFiles = materialFiles.Where(f => f.TargetDir == "testcases").ToList();
        var otherFiles = materialFiles.Where(f => f.TargetDir != "testcases").ToList();

        if (testcaseFiles.Count == 0)
        {
            logger.LogInformation(
                "[Eval] No business testcase files found in template; leaving evaluator test-cases empty for STEP 1.5 synthesis sandboxId={SandboxId}",
                evaluatorSandboxId);
        }

        var allFiles = testcaseFiles.Concat(otherFiles);
        var outlines = new List<EvaluationTestcaseOutline>();

        foreach (var materialFile in allFiles)
        {
            var targetDir = ResolveConsumerMaterialTargetDir(materialFile.TargetDir);
            var uploadResult = await sandboxService.UploadWorkspaceFileAsync(
                new SandboxWorkspaceUploadRequestDto
                {
                    ScopeType = SandboxScopeTypes.Managed,
                    ScopeKey = evaluatorScopeKey,
                    SandboxRole = "evaluation-evaluator",
                    OwnerSubject = owner,
                    SandboxId = evaluatorSandboxId,
                    TargetDir = targetDir,
                    FileName = materialFile.FileName,
                    Content = materialFile.Content,
                    ContentType = materialFile.TargetDir == "testcases" ? "application/json" : "text/plain"
                },
                cancellationToken);

            if (!uploadResult.Success)
            {
                logger.LogWarning(
                    "[Eval] Failed to upload material file sandboxId={SandboxId} dir={Dir} file={File} msg={Message}",
                    evaluatorSandboxId, targetDir, materialFile.FileName, uploadResult.Message);
                continue;
            }

            // 解析 testcase 文件提取轮廓，供前端展示评估场景列表
            if (materialFile.TargetDir == "testcases")
            {
                outlines.AddRange(ParseTestcaseOutlines(materialFile.FileName, materialFile.Content));
            }
        }

        logger.LogInformation(
            "[Eval] Material files uploaded to evaluator sandboxId={SandboxId} testcases={TestcaseCount} other={OtherCount} outlines={Outlines}",
            evaluatorSandboxId, testcaseFiles.Count, otherFiles.Count, outlines.Count);

        return ApiResponse<IReadOnlyList<EvaluationTestcaseOutline>>.SuccessResponse(outlines);
    }

    /// <summary>
    /// 解析 testcase JSON 文件，提取每个用例的 id / title / userRequest 三元组。
    /// 支持顶层数组、{ "test_cases": [...] } 两种格式，与 Python parse_testcases 逻辑对齐。
    /// </summary>
    private static IEnumerable<EvaluationTestcaseOutline> ParseTestcaseOutlines(
        string fileName,
        byte[] content)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            IEnumerable<JsonElement> items = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.EnumerateArray(),
                JsonValueKind.Object when doc.RootElement.TryGetProperty("test_cases", out var nested)
                    && nested.ValueKind == JsonValueKind.Array => nested.EnumerateArray(),
                JsonValueKind.Object when doc.RootElement.TryGetProperty("cases", out var cases)
                    && cases.ValueKind == JsonValueKind.Array => cases.EnumerateArray(),
                JsonValueKind.Object => [doc.RootElement],
                _ => []
            };

            var index = 0;
            foreach (var item in items)
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object) continue;

                var id = GetStringProperty(item, "test_case_id", "testcase_id", "case_id", "id") ?? $"TC-{index:D3}";
                var title = GetStringProperty(item, "scenario_name", "title", "name") ?? $"场景 {index}";
                var userRequest = item.TryGetProperty("input", out var inputEl) && inputEl.ValueKind == JsonValueKind.Object
                    ? GetStringProperty(inputEl, "opening_message", "user_message", "user_request", "prompt") ?? string.Empty
                    : GetStringProperty(item, "prompt") ?? string.Empty;

                yield return new EvaluationTestcaseOutline(id, title, userRequest);
            }
        }
    }

    private IReadOnlyList<(string FileName, byte[] Content)> BuildConsumerTestCaseFiles(
        IReadOnlyList<TestcaseSourceFile> testcaseSources,
        EmployeeDetailDto employee)
    {
        var results = new List<(string FileName, byte[] Content)>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceIndex = 0;

        foreach (var source in testcaseSources)
        {
            sourceIndex++;
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(source.RawJson);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "[Eval] Failed to parse testcase source file {FileName}", source.FileName);
                continue;
            }

            using (doc)
            {
                var caseIndex = 0;
                foreach (var item in EnumerateTestcaseItems(doc.RootElement))
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    caseIndex++;
                    var rawId = GetStringProperty(item, "test_case_id", "testcase_id", "case_id", "id")
                                ?? $"{Path.GetFileNameWithoutExtension(source.FileName)}-{caseIndex}";
                    var testCaseId = NormalizeUniqueConsumerTestCaseId(rawId, sourceIndex, caseIndex, usedIds);
                    var title = GetStringProperty(item, "scenario_name", "title", "name") ?? $"Scenario {caseIndex}";
                    var openingMessage = ResolveConsumerOpeningMessage(item, title);
                    var payload = BuildConsumerTestCasePayload(testCaseId, title, openingMessage, item, employee);
                    var content = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
                    results.Add(($"{testCaseId}.tc.json", content));
                }
            }
        }

        return results;
    }

    private static IEnumerable<JsonElement> EnumerateTestcaseItems(JsonElement root)
    {
        return root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray(),
            JsonValueKind.Object when root.TryGetProperty("test_cases", out var nested)
                && nested.ValueKind == JsonValueKind.Array => nested.EnumerateArray(),
            JsonValueKind.Object when root.TryGetProperty("cases", out var cases)
                && cases.ValueKind == JsonValueKind.Array => cases.EnumerateArray(),
            JsonValueKind.Object => [root],
            _ => []
        };
    }

    private static Dictionary<string, object?> BuildConsumerTestCasePayload(
        string testCaseId,
        string title,
        string openingMessage,
        JsonElement source,
        EmployeeDetailDto employee)
    {
        var input = new Dictionary<string, object?>
        {
            ["opening_message"] = openingMessage,
            ["goal"] = new Dictionary<string, object?>
            {
                ["primary"] = ResolveConsumerGoal(source, openingMessage)
            },
            ["stop_conditions"] = new Dictionary<string, object?>
            {
                ["success"] = "User request is resolved with a clear next step.",
                ["failure"] = "The employee violates business rules or cannot continue.",
                ["deadlock"] = "No useful new information appears across multiple turns."
            }
        };

        if (source.TryGetProperty("input", out var inputElement) &&
            inputElement.ValueKind == JsonValueKind.Object &&
            inputElement.TryGetProperty("context", out var contextElement) &&
            contextElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            input["context"] = contextElement.Clone();
        }

        var expectedOutput = new Dictionary<string, object?>
        {
            ["expected_response_traits"] = ExtractConsumerExpectedResponseTraits(source),
            ["expected_outcomes"] = ExtractConsumerExpectedOutcomes(source)
        };

        return new Dictionary<string, object?>
        {
            ["test_case_id"] = testCaseId,
            ["version"] = "1.0.0",
            ["title"] = title,
            ["description"] = $"Converted from HireBot evaluation material: {title}",
            ["applicable_roles"] = new[] { "*" },
            ["applicable_scenarios"] = new[] { "*" },
            ["difficulty"] = "medium",
            ["tags"] = new[] { FirstNonEmpty(employee.SourceTemplateId, employee.RoleName, "hirebot"), "hirebot-generated" },
            ["input"] = input,
            ["turn_budget"] = new Dictionary<string, object?>
            {
                ["hard_max_turns"] = 8,
                ["soft_target_turns"] = 4
            },
            ["expected_output"] = expectedOutput
        };
    }

    private static string ResolveConsumerOpeningMessage(JsonElement source, string fallbackTitle)
    {
        if (source.TryGetProperty("input", out var inputElement) &&
            inputElement.ValueKind == JsonValueKind.Object)
        {
            var message = GetStringProperty(inputElement, "opening_message", "user_message", "user_request", "prompt");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return GetStringProperty(source, "prompt", "description") ?? fallbackTitle;
    }

    private static string ResolveConsumerGoal(JsonElement source, string fallback)
    {
        if (source.TryGetProperty("expected_output", out var expectedElement) &&
            expectedElement.ValueKind == JsonValueKind.Object)
        {
            var resolution = GetStringProperty(expectedElement, "resolution", "summary");
            if (!string.IsNullOrWhiteSpace(resolution))
            {
                return resolution;
            }
        }

        return fallback;
    }

    private static IReadOnlyList<string> ExtractConsumerExpectedResponseTraits(JsonElement source)
    {
        if (!source.TryGetProperty("expected_behavior_sequence", out var stepsElement) ||
            stepsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var traits = new List<string>();
        foreach (var step in stepsElement.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var criteria = GetStringProperty(step, "criteria", "action");
            if (!string.IsNullOrWhiteSpace(criteria))
            {
                traits.Add(criteria);
            }
        }

        return traits;
    }

    private static IReadOnlyList<string> ExtractConsumerExpectedOutcomes(JsonElement source)
    {
        if (!source.TryGetProperty("expected_output", out var expectedElement) ||
            expectedElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var outcomes = new List<string>();
        foreach (var name in new[] { "resolution", "user_satisfaction" })
        {
            var value = GetStringProperty(expectedElement, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                outcomes.Add(value);
            }
        }

        return outcomes;
    }

    private static string NormalizeUniqueConsumerTestCaseId(
        string value,
        int sourceIndex,
        int caseIndex,
        HashSet<string> usedIds)
    {
        var baseId = NormalizeConsumerPathSegment(value, $"tc-{sourceIndex}-{caseIndex}");
        var candidate = baseId;
        var suffix = 2;
        while (!usedIds.Add(candidate))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        return candidate;
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }
        return null;
    }

    private static string ResolveConsumerMaterialTargetDir(string targetDir)
    {
        return string.Equals(targetDir, "testcases", StringComparison.OrdinalIgnoreCase)
            ? EvaluationConsumerTestCasesTargetDir
            : EvaluationConsumerOntologyTargetDir;
    }

    /// <summary>
    /// 将员工模板包 ZIP 存入本地缓存目录，供后续读取测试用例等。
    /// 返回缓存文件的本地绝对路径；若写入失败则返回 null。
    /// </summary>
    private async Task<string?> PersistUploadedTemplatePackageArchiveAsync(
        string fileName,
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        var cacheRoot = HireBotPathResolver.ResolveEvaluationTemplatePackageCacheRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"]);

        try
        {
            Directory.CreateDirectory(cacheRoot);
            var sanitizedName = HiringAssetFileSystem.SanitizePathSegment(
                Path.GetFileNameWithoutExtension(fileName));
            var cacheFilePath = Path.Combine(cacheRoot, $"{sanitizedName}.zip");
            await File.WriteAllBytesAsync(cacheFilePath, archiveBytes, cancellationToken);

            logger.LogInformation(
                "[Eval] Template package archive cached path={CachePath} size={Size}",
                cacheFilePath, archiveBytes.Length);

            return cacheFilePath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Eval] Failed to persist template package archive to cache. fileName={FileName}", fileName);
            return null;
        }
    }

    /// <summary>
    /// 根据 fixture 绑定关系解析员工模板包的本地目录路径。
    /// 优先查找 TemplatePackages root 中的目录，回退到 InstanceFixtures 中 fixture 实例的 template 子目录。
    /// </summary>
    private string? ResolveBoundFixtureTemplatePackageRoot(
        string templateId,
        EmployeeDetailDto employee,
        FixtureTemplateBinding fixtureBinding)
    {
        var effectiveTemplateId = fixtureBinding.FixtureTemplateId ?? templateId;
        var configuredRoot = configuration["HireBot:TemplatePackagesRoot"];
        var packagesRoot = HiringAssetFileSystem.ResolveDirectory(
            hostEnvironment.ContentRootPath,
            configuredRoot,
            Path.Combine("Assets", "TemplatePackages"));

        var candidatePath = Path.Combine(
            packagesRoot,
            HiringAssetFileSystem.SanitizePathSegment(effectiveTemplateId));
        if (Directory.Exists(candidatePath))
            return candidatePath;

        // 回退：在 InstanceFixtures 中查找 fixture 实例自带的 template 子目录
        var fixtureRoot = ResolveFixtureRoot();
        if (fixtureRoot is not null && !string.IsNullOrWhiteSpace(fixtureBinding.FixtureEmployeeId))
        {
            var fixtureInstanceTemplatePath = Path.Combine(
                fixtureRoot,
                fixtureBinding.FixtureEmployeeId.Trim(),
                "template");
            if (Directory.Exists(fixtureInstanceTemplatePath))
                return fixtureInstanceTemplatePath;
        }

        return null;
    }

    /// <summary>
    /// 将测试用例和本体规则打包成 ZIP，上传到 evaluator sandbox 的 consumer 材料目录。
    /// evaluator skill 通过文件系统路径直接读取，不再经过媒体缓存中转。
    /// </summary>
    private async Task<ApiResponse<string>> PrepareEvaluatorMaterialsArchiveAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext ctx,
        CancellationToken cancellationToken)
    {
        var testcaseSources = await LoadTestcaseSourcesAsync(ctx, employee, cancellationToken);

        if (testcaseSources.Count == 0)
        {
            logger.LogInformation(
                "[Eval] No business testcase sources found; materials.zip will keep test-cases empty for STEP 1.5 synthesis sandboxId={SandboxId}",
                ctx.EvaluatorSandboxId);
        }
        var ontologyProfile = await BuildOntologyProfileAsync(ctx, employee, cancellationToken);

        // 将测试用例和本体打包为 ZIP，ZIP 发送后 gateway 会自动解压到 workspace 目录
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("test-cases/");
            archive.CreateEntry("ontology/");

            // testcases.json
            var testcasesPayload = JsonSerializer.Serialize(
                testcaseSources.Select(s => new
                {
                    file_name = s.FileName,
                    source_path = s.SourcePath,
                    raw_json = s.RawJson,
                    source_type = s.SourceType
                }),
                JsonOptions);
            // 每个 entry 的 stream 必须在创建下一个 entry 之前关闭，否则 ZipArchive 会抛异常
            var tcEntry = archive.CreateEntry("testcases.json");
            await using (var tcStream = tcEntry.Open())
            {
                await tcStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(testcasesPayload), cancellationToken);
            }

            foreach (var testCaseFile in BuildConsumerTestCaseFiles(testcaseSources, employee))
            {
                var consumerTcEntry = archive.CreateEntry($"test-cases/{testCaseFile.FileName}");
                await using var consumerTcStream = consumerTcEntry.Open();
                await consumerTcStream.WriteAsync(testCaseFile.Content, cancellationToken);
            }

            // ontology.json
            var ontologyPayload = JsonSerializer.Serialize(new
            {
                version = "ontology-v2",
                generated_at_utc = DateTimeOffset.UtcNow.ToString("o"),
                source_summary = ontologyProfile.SourceSummary,
                dimension_weights = ontologyProfile.DimensionWeights,
                rules = ontologyProfile.DimensionRules
            }, JsonOptions);
            var ontoEntry = archive.CreateEntry("ontology/ontology.json");
            await using (var ontoStream = ontoEntry.Open())
            {
                await ontoStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(ontologyPayload), cancellationToken);
            }
        }

        var archiveBytes = ms.ToArray();
        if (archiveBytes.Length == 0)
            return ApiResponse<string>.ErrorResponse(422, "materials archive is empty");

        var uploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = ctx.EvaluatorHireId,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                SandboxId = ctx.EvaluatorSandboxId,
                TargetDir = EvaluationConsumerMaterialUploadRoot,
                FileName = "materials.zip",
                Content = archiveBytes,
                ContentType = "application/zip"
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
            return ApiResponse<string>.ErrorResponse(uploadResult.Code, uploadResult.Message);

        logger.LogInformation(
            "[Eval] Materials archive uploaded to evaluator workspace sandboxId={SandboxId} dir={WorkspaceDir} testcases={TestcaseCount} fileCount={FileCount}",
            ctx.EvaluatorSandboxId, uploadResult.Data.WorkspaceDir, testcaseSources.Count, uploadResult.Data.FileCount);

        return ApiResponse<string>.SuccessResponse(
            uploadResult.Data.WorkspaceDir,
            "evaluator materials uploaded to workspace");
    }
}
