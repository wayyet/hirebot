using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
using HireBot.Core.Services.Hiring;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
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
        var isPrivateBranch = string.Equals(employee.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase);
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

        // 注意：评估有多种入口。私有分支是特殊模型：它不创建新实例、不创建新沙箱，
        // 五件套直接原地更新到个人分身 runtime 沙箱里。因此私有分支评估的 target
        // 必须复用当前实例的 runtime 沙箱，不能再创建 evaluation-target。
        //
        // 非私有分支（雇佣员工/普通评估）必须保持原来的双沙箱评估流程：
        // evaluation-target + evaluation-evaluator，避免影响正式雇佣评估链路。
        var stepStates = new Dictionary<string, WorkspaceStepState>(StringComparer.OrdinalIgnoreCase)
        {
            ["target_sandbox"] = new("running", null)
        };

        var targetResult = isPrivateBranch
            ? await ResolveTargetRuntimeSandboxAsync(owner, employeeId, cancellationToken)
            : await CreateEvaluationSandboxAsync(owner, employeeId, "evaluation-target", useStableRuntimeId: false, cancellationToken);
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

        // Create evaluator sandbox directly via native sandbox API
        // 私有分支只定制当前用户自己的分身，评估 evaluator 可以稳定复用，避免反复启动沙箱。
        // 普通/雇佣评估仍使用原有随机 runtimeId，保持原评估隔离语义不变。
        var evaluatorResult = await CreateEvaluationSandboxAsync(
            owner,
            employeeId,
            "evaluation-evaluator",
            useStableRuntimeId: isPrivateBranch && !forceTargetHireRecreate,
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

        // 濠电偞鍨堕幐鎼佹晝閿濆洦顫曢柛鎾茬劍鐎氭岸鏌涢弴銊ょ盎鐎殿喖鍢查埥澶愬箻鐎涙ê纰嶅銈嗘煥缁夌鐏掗梺鏂ユ櫅閸燁垳娆㈤弻銉︾厱闁哄诞鍕闂佺硶鏅涢惉濂稿箯閻樿绀嬫い蹇撳閹搞倝姊洪崗鍏肩凡闁瑰啿绻橀幆灞解枎閹寸姳绗?
        stepStates["upload_skill"] = new("running", null);
        var uploadResult = await UploadSkillToSandboxAsync(evaluatorSandboxId, owner, cancellationToken);
        if (!uploadResult.Success)
        {
            stepStates["upload_skill"] = new("failed", uploadResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(uploadResult.Code, uploadResult.Message);
        }

        stepStates["upload_skill"] = new("completed", null);

        // 濠电偞鍨堕幐鎼佹晝閿濆洦顫曢柛顐ｆ礃閸庡孩銇勯弮鍌涙珪闁搞劌銈搁弻娑樷槈濞咁収浜濈€靛ジ骞囬鈺冨枛閸╁嫰宕橀埡鍐ㄥ殥闂備礁鎲＄敮妤佺珶閸℃鐑樺閺夋垵鍞ㄩ梺鎼炲劘閸斿酣鎮￠幘鍓佺＜闁绘ɑ褰冪紞浣虹磼濡も偓閻ジ骞忛悩璇茬妞ゅ繐瀚幐銈呪攽閻愬弶婀伴柣鐔濆浂鏁?
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

        // 闂佽绻愮换鎰涘▎鎺戞殲闂備礁鎼ˇ顖炲Φ濡椿娈介柛銉墯閸嬪鏌嶇悰鈥充壕缂備焦顨呴崐鍦偓闈涖偢閹晠骞撻幒鏂垮笓闂備胶鍎甸弲鈺呭窗濡ゅ懏鍋夐柨婵嗘噳閺岋附绻涢崱妯虹劸闁哥偟顭堥埥澶愬箻閾忣偄鏀梺浼欑畱鐎涒晝鈧潧銈搁幃褔宕奸姀銏犲箚缂傚倷鑳舵慨鐢稿船閼姐倖顫曟繝闈涱儐閻掑ジ鏌涢…鎴濇灈閻㈩垱濞婇幃褰掑炊瑜嶉褔鏌涘▎蹇ユ敾缂佹鍠曢ˇ鏌ユ煕閻旀彃浜鹃梻浣烘嚀閸㈡煡藝椤栨縿浜归柛銉墮缁狙囨煙闁箑骞楅柣蹇斿姍閺?
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

    private async Task<ApiResponse<(string RuntimeId, string SandboxId)>> ResolveTargetRuntimeSandboxAsync(
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        // 私有分支 target 复用个人分身运行时沙箱。
        // 如果这里找不到 runtime 沙箱，说明该分身还没初始化过站内对话/运行时，
        // 需要先进入一次对话页触发 runtime 沙箱创建。
        var runtimeScopeKey = $"instance:{employeeId.Trim()}";
        var instance = await dbContext.SandboxInstances
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == owner &&
                item.ScopeType == SandboxScopeTypes.Hire &&
                item.ScopeKey == runtimeScopeKey &&
                item.SandboxRole == "runtime" &&
                item.State != "Deleted")
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (instance is null)
        {
            return ApiResponse<(string, string)>.ErrorResponse(
                409,
                "target runtime sandbox not found; open the employee chat once to initialize its runtime sandbox");
        }

        var refresh = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                SandboxId = instance.SandboxId,
                OwnerSubject = owner
            },
            cancellationToken);
        if (!refresh.Success || refresh.Data is null)
        {
            return ApiResponse<(string, string)>.ErrorResponse(refresh.Code, refresh.Message);
        }

        if (!string.Equals(refresh.Data.State, "Running", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(refresh.Data.GatewayEndpoint))
        {
            return ApiResponse<(string, string)>.ErrorResponse(
                409,
                "target runtime sandbox gateway endpoint not ready");
        }

        return ApiResponse<(string, string)>.SuccessResponse((employeeId.Trim(), refresh.Data.SandboxId));
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

    private async Task<ApiResponse<bool>> UploadSkillToSandboxAsync(
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

        var manifestPath = Path.Combine(templateRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return ApiResponse<bool>.ErrorResponse(422, $"evaluation template package manifest missing: {manifestPath}");
        }

        var bundle = await ZipDirectoryAsBundleAsync(
            templateRoot,
            $"{templatePackageId}.zip",
            sourceType: "evaluation-template-package",
            cancellationToken);
        if (bundle.Content.Length == 0)
        {
            return ApiResponse<bool>.ErrorResponse(422, "evaluation template package archive is empty");
        }

        var uploadResult = await sandboxService.UploadSkillPackageAsync(
            new SkillPackageUploadRequestDto
            {
                SandboxId = sandboxId,
                OwnerSubject = owner,
                ArchiveBytes = bundle.Content,
                FileName = bundle.FileName
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
            return ApiResponse<bool>.ErrorResponse(uploadResult.Code, uploadResult.Message);

        logger.LogInformation("[Eval] Evaluation template package uploaded sandboxId={SandboxId} installed={Count}",
            sandboxId, uploadResult.Data.SkillsInstalled);
        return ApiResponse<bool>.SuccessResponse(true, "evaluation template package uploaded");
    }

    /// <summary>
    /// 闂佽绻愮换鎰涘Δ鍛疅闁告劕妯婇崯鍛存煏婢跺牆鈧繈鎮伴幘缁樼厽婵＄偟绮▍鍛存倵閸倖鎴犵矙婢跺绡€濞达綀娅ｉ崐鐐烘⒑閸涘﹤绗у褎顨堥埀顒€鐏氬銊╁焵椤掑喚娼愮痪鏉跨Ч閹苯鈻庨幋鐘辩瑝闁哄鐗冮弲婊堟偩?/admin/digital-employee/upload 闂備浇顫夋禍浠嬪磿鏉堫偁浜规繛鎴欏灪閺?
    /// 闂備焦妞垮鍧楀礉鐎ｎ剝濮虫い鎺戝€规刊濂告煃閸濆嫬鏆炵紒鎰殜閺屸€愁吋閸涱喖顦╅梺娲诲幗閻熝呭垝婵犳艾鐭楁俊顖濆亹閹插潡鏌ｉ悩鍙夋悙閻庢凹浜畷锝嗙節閸屾鐓㈠┑鐐叉閸╁牓宕幖浣圭厵闂傗偓閹邦喖濡藉┑鐘亾濞撴埃鍋撶€规洏鍎虫禒锕傛嚃閳哄﹣绱樺┑鐐差嚟婵偓鎱ㄩ妶鍫濇殲闂備礁鎼ˇ鎵偓绗涘喚鐒芥俊銈呮噹濡ɑ绻涢崱妤冃滈柛?
    /// </summary>
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
        var uploadFromBundleResult = await sandboxService.UploadSkillPackageAsync(
            new SkillPackageUploadRequestDto
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

        /*

        // 濠电偞娼欓崥瀣晪闂佸憡蓱缁嬫帡骞忛崨顖涘磯闁靛闄勫▓銏ゆ⒑閸濆嫬顏柛搴＄－濡叉劙鏁撻悩鑼唶闂佹悶鍎滈崨顔界槥闂備焦鐪归崝宀€鈧凹浜濋〃銉╁炊椤掍礁浜遍梺鍐叉惈鐎氼噣鎮㈤崨顖楀亾?
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
                archiveBytes = bundle.Content;
                fileName = bundle.FileName;
            }
            else if (File.Exists(normalizedPath) && normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                archiveBytes = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
                fileName = Path.GetFileName(normalizedPath);
            }
            else
            {
                return ApiResponse<bool>.ErrorResponse(404, $"explicit artifact path not found: {normalizedPath}");
            }
        }
        else
        {
            // 濠电偛顕慨鎾煀閿濆應鏋栫憸鏃堝箖娴犲惟闁靛牆顦卞畷婊堟⒑缂佹ǜ浠滈柡鍛〒閳ь剙鐏氶敃銏狀嚕闂堟侗鍚嬮柛娑卞弾濞兼娊姊洪崗鐓庡姢闁搞垼灏妵鎰板炊椤掍焦娅栭梺鍓插亝缁牏鑺?EmployeeId 濠电偠鎻徊鍓у垝閸垺瀚?hireId 闂備礁鎼悮顐﹀磿閸欏鐝舵慨妞诲亾闁?
            var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(employee.EmployeeId, cancellationToken);
            if (packageSnapshot?.Content is { Length: > 0 })
            {
                archiveBytes = packageSnapshot.Content;
                fileName = string.IsNullOrWhiteSpace(packageSnapshot.FileName)
                    ? $"hiring_artifacts_{employee.EmployeeId}.zip"
                    : packageSnapshot.FileName;
            }
            else
            {
                // 闂備焦鎮堕崕鎶藉磻閵堝鐒垫い鎴ｆ娴滈箖姊洪崨濠傜濠⒀勵殔閿曘垺瀵奸弶鎴炲祶?fixture 闂備胶鍎甸弲鈺呭窗閺嶎偆绀?
                var fixtureDir = ResolveFixtureArtifactDirectory(employee.EmployeeId, employee);
                if (string.IsNullOrWhiteSpace(fixtureDir))
                {
                    return ApiResponse<bool>.ErrorResponse(404, $"no artifact package or fixture directory found for employee {employee.EmployeeId}");
                }

                var bundle = await ZipDirectoryAsBundleAsync(
                    fixtureDir,
                    $"fixture_{employee.EmployeeId}.zip",
                    sourceType: "fixture",
                    cancellationToken);
                archiveBytes = bundle.Content;
                fileName = bundle.FileName;
            }
        }

        if (archiveBytes.Length == 0)
        {
            return ApiResponse<bool>.ErrorResponse(422, "artifact archive is empty");
        }

        var uploadResult = await sandboxService.UploadSkillPackageAsync(
            new SkillPackageUploadRequestDto
            {
                SandboxId = sandboxId,
                OwnerSubject = owner,
                ArchiveBytes = archiveBytes,
                FileName = fileName
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
        {
            return ApiResponse<bool>.ErrorResponse(uploadResult.Code, uploadResult.Message);
        }

        logger.LogInformation(
            "[Eval] {SandboxSide} artifact uploaded sandboxId={SandboxId} fileName={FileName} installed={Count}",
            sandboxSide,
            sandboxId,
            fileName,
            uploadResult.Data.SkillsInstalled);
        return ApiResponse<bool>.SuccessResponse(true, $"{sandboxSide} artifact uploaded");
        */
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
    /// 闂佽绻愮换鎰涘☉妯忕儤瀵奸弶鎴濆敤闂佹悶鍎滈崟顐ｇ€柣搴ゎ潐閹爼宕曢崘娴嬫灁闁硅揪绠戦弸渚€鏌℃径搴㈢《闁圭晫鍠栭弻娑樷槈濞咁収浜濈€靛ジ骞囬鈺冨枛閸╁嫰宕橀埡鍐ㄥ殥濠电偞鍨堕幐鎼佹晝閿濆洦顫曢柛顐ｆ礀缁€鍡涙煙濞堝灝鏋熺紒鎰殜閺屸€愁吋閸涱喖顦╅梺娲诲幗閻熝呭垝婵犳艾鐭楁俊顖濆亹閹插潡鏌ｉ悩鍙夋悙閻庢凹浜畷锝嗙節閸屾鐓㈠┑鐐叉閸╁牓宕幖浣圭厪?
    /// 闂備胶鍎甸弲鈺呭窗濡ゅ懏鍋夐柨婵嗘噳閺岋附绻涢崱妯虹劸闁哥偞鎮傚濠氬炊閿濆懍澹曢梺鑽ゅ枑濞叉垿鎮為敃浣告殲闂備礁鎼ˇ鎵偓绗涘喚鐒介柣銏㈩焾缁犮儳鎲搁幋锔衡偓渚€骞嬮悙纰樻灃濠殿喗锕╅崜娆擄綖閵堝鈷戞い鎰剁稻椤绱掓０婵嗕喊闁轰礁绉撮悾婵嬪礃椤忓拋娼犲┑鐐殿棎閸嬫劖鏅跺Δ鍛剹婵炲棙鍨规稉宥夋⒑椤掆偓缁夊灚绂掑鈧幃鐑藉即濮樺崬濡介柤鍨涙櫊閺屸剝寰勭€ｎ亶鍤嬪┑鐐靛帶閻忔氨绮嬪澶婂耿婵絾瀵х敮鈥崇暦閵娿儙鐔告姜閹殿喚鐓戦梻浣哄帶閻ゅ洤螞閸曨剚鍙忛煫鍥ㄧ☉杩?
    /// </summary>
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

        // 濠电偞鍨堕幐鎼佹晝閿濆洦顫曢柛顐ｆ礀缁€鍡涙煙濞堝灝鏋熺紒鎰殜閺屸€愁吋閸涱喖顦╅梺娲诲幗閻熝呭垝?
        var targetUploadResult = await sandboxService.UploadSkillPackageAsync(
            new SkillPackageUploadRequestDto
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

