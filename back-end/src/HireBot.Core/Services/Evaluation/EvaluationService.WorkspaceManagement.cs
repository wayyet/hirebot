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
            logger.LogInformation(
                "[Eval] Reusing cached workspace employeeId={EmployeeId} targetSandboxId={TargetSandboxId} evaluatorSandboxId={EvaluatorSandboxId}",
                employeeId,
                cachedWorkspace.TargetSandboxId,
                cachedWorkspace.EvaluatorSandboxId);
            return ApiResponse<EvaluationWorkspaceContext>.SuccessResponse(cachedWorkspace);
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
        var employeeTemplateResult = await UploadEmployeeTemplateToSandboxAsync(
            targetSandboxId, evaluatorSandboxId, evaluatorRuntimeId, owner, employee, cancellationToken);
        if (!employeeTemplateResult.Success)
        {
            stepStates["upload_employee_template"] = new("failed", employeeTemplateResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(employeeTemplateResult.Code, employeeTemplateResult.Message);
        }

        workspaceContext = workspaceContext with
        {
            EvaluatorTemplatePackageZipPath = employeeTemplateResult.Data?.SandboxTemplatePackageZipPath,
            UploadedTemplatePackageZipPath = employeeTemplateResult.Data?.UploadedTemplatePackageZipPath
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

        workspaceContext = workspaceContext with
        {
            SkillLoadedAtUtc = DateTimeOffset.UtcNow,
            ArtifactWorkspaceDir = evaluatorArtifactUploadResult.Data
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
                UseCase = $"evaluation-{sandboxRole}-for:{employeeId}"
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

        var archiveBytes = EmployeeHiringService.BuildDigitalEmployeeArchive(templatePackage);
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

    private async Task<ApiResponse<TemplatePackageUploadResult>> UploadEmployeeTemplateToSandboxAsync(
        string targetSandboxId,
        string evaluatorSandboxId,
        string evaluatorRuntimeId,
        string owner,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var templateId = employee.SourceTemplateId?.Trim();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            logger.LogWarning("[Eval] Employee {EmployeeId} has no SourceTemplateId, skipping template upload", employee.EmployeeId);
            return ApiResponse<TemplatePackageUploadResult>.SuccessResponse(
                new TemplatePackageUploadResult(null, null),
                "employee template upload skipped: missing SourceTemplateId");
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
                return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(
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
                return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(
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
                return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(
                    502,
                    $"failed to load template package: {templateId}");
            }
        }

        if (templatePackage.PackageFiles.Count == 0)
        {
            logger.LogWarning("[Eval] Template package {TemplateId} has no files", templateId);
            return ApiResponse<TemplatePackageUploadResult>.SuccessResponse(
                new TemplatePackageUploadResult(null, null),
                "employee template upload skipped: package has no files");
        }

        var archiveBytes = EmployeeHiringService.BuildDigitalEmployeeArchive(templatePackage);
        if (archiveBytes.Length == 0)
        {
            logger.LogError("[Eval] Template archive is empty for templateId={TemplateId}", templateId);
            return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(422, "employee template archive is empty");
        }

        var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
        var uploadedTemplatePackageZipPath = await PersistUploadedTemplatePackageArchiveAsync(
            fileName,
            archiveBytes,
            cancellationToken);

        var targetUploadResult = await sandboxService.UploadDigitalEmployeeTemplateAsync(
            new DigitalEmployeeTemplateUploadRequestDto
            {
                SandboxId = targetSandboxId,
                OwnerSubject = owner,
                ArchiveBytes = archiveBytes,
                FileName = fileName
            },
            cancellationToken);
        if (!targetUploadResult.Success || targetUploadResult.Data is null)
        {
            logger.LogError("[Eval] Failed to upload employee template to target sandboxId={SandboxId} code={Code} msg={Message}",
                targetSandboxId, targetUploadResult.Code, targetUploadResult.Message);
            return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(
                targetUploadResult.Code,
                $"failed to upload employee template to target sandbox: {targetUploadResult.Message}");
        }

        logger.LogInformation("[Eval] Employee template uploaded to target sandboxId={SandboxId} installed={Count}",
            targetSandboxId, targetUploadResult.Data.SkillsInstalled);

        // 将员工模板直接解压到 evaluator workspace/uploads/template/ 目录，
        // evaluator skill 可通过文件系统路径直接读取，无需经过媒体缓存中转。
        var evaluatorUploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = evaluatorRuntimeId,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                SandboxId = evaluatorSandboxId,
                TargetDir = "uploads/template",
                FileName = fileName,
                Content = archiveBytes,
                ContentType = "application/zip"
            },
            cancellationToken);
        if (!evaluatorUploadResult.Success || evaluatorUploadResult.Data is null)
        {
            logger.LogError("[Eval] Failed to upload employee template to evaluator workspace sandboxId={SandboxId} code={Code} msg={Message}",
                evaluatorSandboxId, evaluatorUploadResult.Code, evaluatorUploadResult.Message);
            return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(
                evaluatorUploadResult.Code,
                $"failed to upload employee template to evaluator sandbox workspace: {evaluatorUploadResult.Message}");
        }

        var templatePackageZipPath = evaluatorUploadResult.Data.WorkspaceDir;
        logger.LogInformation(
            "[Eval] Employee template uploaded to evaluator sandbox workspace sandboxId={SandboxId} workspaceDir={WorkspaceDir} fileCount={FileCount} uploadedTemplatePackageZipPath={UploadedTemplatePackageZipPath}",
            evaluatorSandboxId,
            templatePackageZipPath,
            evaluatorUploadResult.Data.FileCount,
            uploadedTemplatePackageZipPath);

        return ApiResponse<TemplatePackageUploadResult>.SuccessResponse(
            new TemplatePackageUploadResult(templatePackageZipPath, uploadedTemplatePackageZipPath),
            "employee template uploaded");
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
    /// 将测试用例和本体规则打包成 ZIP，上传到 evaluator sandbox workspace/uploads/materials/ 目录。
    /// evaluator skill 通过文件系统路径直接读取，不再经过媒体缓存中转。
    /// </summary>
    private async Task<ApiResponse<string>> PrepareEvaluatorMaterialsArchiveAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext ctx,
        EvaluationSessionEntity sessionEntity,
        CancellationToken cancellationToken)
    {
        var testcaseSources = await LoadTestcaseSourcesAsync(ctx, employee, cancellationToken);
        var ontologyProfile = await BuildOntologyProfileAsync(ctx, employee, cancellationToken);

        // 将测试用例和本体打包为 ZIP，ZIP 发送后 gateway 会自动解压到 workspace 目录
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
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
            var tcEntry = archive.CreateEntry("testcases.json");
            await using var tcStream = tcEntry.Open();
            await tcStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(testcasesPayload), cancellationToken);

            // ontology.json
            var ontologyPayload = JsonSerializer.Serialize(new
            {
                version = "ontology-v2",
                generated_at_utc = DateTimeOffset.UtcNow.ToString("o"),
                source_summary = ontologyProfile.SourceSummary,
                dimension_weights = ontologyProfile.DimensionWeights,
                rules = ontologyProfile.DimensionRules
            }, JsonOptions);
            var ontoEntry = archive.CreateEntry("ontology.json");
            await using var ontoStream = ontoEntry.Open();
            await ontoStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(ontologyPayload), cancellationToken);
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
                TargetDir = "uploads/materials",
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
