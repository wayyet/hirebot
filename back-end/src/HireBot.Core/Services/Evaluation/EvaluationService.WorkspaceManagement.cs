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

        if (!forceTargetHireRecreate &&
            EvaluationWorkspaces.TryGetValue(workspaceKey, out var cachedWorkspace) &&
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
            StepStates: stepStates);
        EvaluationWorkspaces[workspaceKey] = workspaceContext;

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
        EvaluationWorkspaces[workspaceKey] = workspaceContext;
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

        workspaceContext = workspaceContext with { SkillLoadedAtUtc = DateTimeOffset.UtcNow };
        EvaluationWorkspaces[workspaceKey] = workspaceContext;

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

    private async Task<ApiResponse<bool>> UploadArtifactAttachmentToSandboxAsync(
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
        {
            return ApiResponse<bool>.ErrorResponse(bundleResult.Code, bundleResult.Message);
        }

        var bundle = bundleResult.Data;
        var (tenantId, operatorId) = ResolveTenantAndOperator(owner);
        var uploadResult = await sandboxService.UploadAttachmentAsync(
            new SandboxAttachmentUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = scopeKey,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                TenantId = tenantId,
                OperatorId = operatorId,
                SandboxId = sandboxId,
                Material = new HiringConversationMaterialDto
                {
                    Type = "artifact-package-zip",
                    Name = bundle.FileName,
                    Content = Convert.ToBase64String(bundle.Content),
                    MimeType = "application/zip",
                    Size = bundle.Content.LongLength,
                    ContentHash = bundle.Sha256,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["contentEncoding"] = "base64",
                        ["sourceType"] = bundle.SourceType,
                        ["sourcePath"] = bundle.SourcePath
                    }
                }
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
        {
            return ApiResponse<bool>.ErrorResponse(uploadResult.Code, uploadResult.Message);
        }

        logger.LogInformation(
            "[Eval] {SandboxSide} artifact attached sandboxId={SandboxId} mediaId={MediaId} fileName={FileName}",
            sandboxSide,
            sandboxId,
            uploadResult.Data.MediaId,
            bundle.FileName);
        return ApiResponse<bool>.SuccessResponse(true, $"{sandboxSide} artifact attached");
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

        // 濠电偞鍨堕幐鎼佹晝閿濆洦顫曢柛顐ｆ礀缁€鍡涙煙瀹勬壆鐏辨俊鍙夊灥闇夐柤娴嬫櫅椤ｅジ鏌ｉ鐔烘噮缂?闂備焦瀵х粙鎺楁儗椤旀儳鍨濋幖娣灪婵瓨绻濇繛鎯т壕闁荤姵鍔楅崰鏍ь潖娴犲绀嬫い蹇撴媼閸嬨劍绻涢幋鐐村碍闁挎洩绲垮Σ鎰板醇閺囩喐娅栭悗鍏夊亾闁告洦浜炵槐姘舵⒑缁嬭法绠扮紒澶屽厴閵嗗懘顢曢敂钘夊壆?workspace 闂備礁鎲￠崝鏇㈠箠鎼淬劌绀勯柨鐔哄Т鐎氬顭跨捄鐑樻拱闁伙綁浜堕幃褰掑炊瑜嶉褔鏌涘▎蹇ュ伐閾伙綁鏌嶉埡浣告殲缂?
        var evaluatorUploadResult = await sandboxService.UploadAttachmentAsync(
            new SandboxAttachmentUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = evaluatorRuntimeId,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                TenantId = "tenant-default",
                OperatorId = "operator-default",
                SandboxId = evaluatorSandboxId,
                Material = new HiringConversationMaterialDto
                {
                    Type = "template-package-zip",
                    Name = fileName,
                    Content = Convert.ToBase64String(archiveBytes),
                    MimeType = "application/zip",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["contentEncoding"] = "base64"
                    }
                }
            },
            cancellationToken);
        if (!evaluatorUploadResult.Success || evaluatorUploadResult.Data is null)
        {
            logger.LogError("[Eval] Failed to upload employee template attachment to evaluator sandboxId={SandboxId} code={Code} msg={Message}",
                evaluatorSandboxId, evaluatorUploadResult.Code, evaluatorUploadResult.Message);
            return ApiResponse<TemplatePackageUploadResult>.ErrorResponse(
                evaluatorUploadResult.Code,
                $"failed to upload employee template attachment to evaluator sandbox: {evaluatorUploadResult.Message}");
        }

        var templatePackageZipPath = ResolveMediaCachePathFromAttachment(evaluatorUploadResult.Data);
        logger.LogInformation(
            "[Eval] Employee template uploaded to evaluator sandbox attachment sandboxId={SandboxId} mediaId={MediaId} marker={Marker} templatePackageZipPath={TemplatePackageZipPath} uploadedTemplatePackageZipPath={UploadedTemplatePackageZipPath}",
            evaluatorSandboxId,
            evaluatorUploadResult.Data.MediaId,
            evaluatorUploadResult.Data.Marker,
            templatePackageZipPath,
            uploadedTemplatePackageZipPath);

        return ApiResponse<TemplatePackageUploadResult>.SuccessResponse(
            new TemplatePackageUploadResult(templatePackageZipPath, uploadedTemplatePackageZipPath),
            "employee template uploaded");
    }

    private static string? ResolveBoundFixtureTemplatePackageRoot(
        string templateId,
        EmployeeDetailDto employee,
        FixtureTemplateBinding binding)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return null;
        }

        var candidates = new List<string>();

        static void AddCandidate(List<string> list, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            list.Add(Path.GetFullPath(candidate.Trim()));
        }

        AddCandidate(candidates, Path.Combine(fixtureRoot, templateId));
        AddCandidate(candidates, ResolveFixtureArtifactDirectory(employee.EmployeeId, employee));

        if (!string.IsNullOrWhiteSpace(binding.FixtureEmployeeId))
        {
            var fixtureEmployeeId = binding.FixtureEmployeeId.Trim();
            AddCandidate(candidates, Path.Combine(fixtureRoot, fixtureEmployeeId));
            if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
            {
                AddCandidate(candidates, Path.Combine(fixtureRoot, $"hire_{fixtureEmployeeId[2..]}"));
            }
        }

        if (!string.IsNullOrWhiteSpace(binding.FixtureTemplateId))
        {
            AddCandidate(candidates, Path.Combine(fixtureRoot, binding.FixtureTemplateId.Trim()));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path =>
                Directory.Exists(path) &&
                File.Exists(Path.Combine(path, "manifest.json")));
    }

    private static string ResolveMediaCachePathFromAttachment(SandboxAttachmentUploadResultDto attachment)
    {
        if (TryExtractMediaCachePath(attachment.Marker, out var markerPath))
        {
            return markerPath;
        }

        if (TryExtractMediaCachePath(attachment.Url, out var urlPath))
        {
            return urlPath;
        }

        return $"/app/memory/media-cache/{attachment.MediaId}";
    }

    private static bool TryExtractMediaCachePath(string? candidate, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var value = candidate.Trim();
        const string markerPrefix = "[FILE_URL:";
        if (value.StartsWith(markerPrefix, StringComparison.OrdinalIgnoreCase) &&
            value.EndsWith(']'))
        {
            value = value[markerPrefix.Length..^1].Trim();
        }

        if (value.StartsWith("/app/memory/media-cache/", StringComparison.OrdinalIgnoreCase))
        {
            path = value;
            return true;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.AbsolutePath.StartsWith("/app/memory/media-cache/", StringComparison.OrdinalIgnoreCase))
        {
            path = uri.AbsolutePath;
            return true;
        }

        return false;
    }

    private async Task<string?> PersistUploadedTemplatePackageArchiveAsync(
        string fileName,
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        if (archiveBytes.Length == 0)
        {
            return null;
        }

        try
        {
            var cacheRoot = HireBotPathResolver.ResolveEvaluationTemplatePackageCacheRoot(
                hostEnvironment.ContentRootPath,
                configuration["HireBot:DataRoot"]);
            Directory.CreateDirectory(cacheRoot);

            var hash = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
            var safeName = string.IsNullOrWhiteSpace(fileName)
                ? "template-package"
                : Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".zip";
            }

            var targetPath = Path.Combine(cacheRoot, $"{safeName}-{hash[..12]}{extension}");
            await File.WriteAllBytesAsync(targetPath, archiveBytes, cancellationToken);
            return targetPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Eval] Failed to persist uploaded template package archive for testcase fallback.");
            return null;
        }
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

    private async Task<ApiResponse<ConversationRuntimeContextPayload>> BuildConversationRuntimeContextPayloadAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        bool includeMaterials,
        CancellationToken cancellationToken)
    {
        var targetGatewayEndpoint = await dbContext.SandboxInstances
            .AsNoTracking()
            .Where(item => item.SandboxId == workspaceContext.TargetSandboxId)
            .Select(item => item.GatewayEndpoint)
            .FirstOrDefaultAsync(cancellationToken);
        targetGatewayEndpoint = targetGatewayEndpoint?.Trim();
        if (string.IsNullOrWhiteSpace(targetGatewayEndpoint))
        {
            return ApiResponse<ConversationRuntimeContextPayload>.ErrorResponse(409, "target sandbox gateway endpoint not ready");
        }

        string? explicitMaterialsPath = null;
        if (includeMaterials)
        {
            var materialsResult = await PrepareEvaluatorMaterialsArchiveAsync(
                owner,
                employee,
                workspaceContext,
                sessionEntity,
                cancellationToken);
            if (materialsResult.Success && !string.IsNullOrWhiteSpace(materialsResult.Data))
            {
                explicitMaterialsPath = materialsResult.Data;
            }
            else
            {
                logger.LogWarning(
                    "[Eval] Conversation runtime context could not attach evaluator materials. EmployeeId={EmployeeId} SessionId={SessionId} Message={Message}",
                    employee.EmployeeId,
                    sessionEntity.SessionId,
                    materialsResult.Message);
            }
        }

        var runtimeContextJson = BuildRuntimeContextJson(
            employee,
            workspaceContext,
            sessionEntity,
            targetGatewayEndpoint,
            explicitMaterialsPath);
        return ApiResponse<ConversationRuntimeContextPayload>.SuccessResponse(
            new ConversationRuntimeContextPayload(
                RuntimeContextJson: runtimeContextJson,
                RuntimeContextDefaultPath: "/workspace/runtime/evaluation-context.json",
                TargetGatewayEndpoint: targetGatewayEndpoint,
                TargetHttpBaseUrl: ResolveHttpBaseUrl(targetGatewayEndpoint),
                MaterialsAttached: !string.IsNullOrWhiteSpace(explicitMaterialsPath)));
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
        var runtimeContextResult = await BuildConversationRuntimeContextPayloadAsync(
            owner,
            employee,
            workspaceContext,
            sessionEntity,
            includeMaterials: testcaseReady && ontologyReady,
            cancellationToken);
        if (!runtimeContextResult.Success || runtimeContextResult.Data is null)
        {
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(runtimeContextResult.Code, runtimeContextResult.Message);
        }

        var runtimeContext = runtimeContextResult.Data;
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

            if (HasEvaluationReadyPrompt(readyTimelineResult.Data.Messages) &&
                HasTargetSandboxContextPrompt(readyTimelineResult.Data.Messages))
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
                    Content = "In your next response, first output exactly `目标沙箱连接上下文已就绪。` Then explain briefly that the target sandbox connection metadata is already available in this conversation and you can use the attached runtime_context.json plus internal auth logic to connect to the target sandbox without asking the user for endpoint or token. If a tool or script expects /workspace/runtime/evaluation-context.json, create /workspace/runtime and write the provided runtime context there first. After that, show the question cards and scoring hints.",
                    StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["evaluation_context_ready"] = "true",
                        ["question_cards_markdown"] = questionCardsMarkdown,
                        ["question_cards_announced"] = "false",
                        ["ontology_rules_markdown"] = ontologyRulesMarkdown,
                        ["target_context_ready"] = "true",
                        ["runtime_context_json"] = runtimeContext.RuntimeContextJson,
                        ["runtime_context_default_path"] = runtimeContext.RuntimeContextDefaultPath,
                        ["target_sandbox_id"] = workspaceContext.TargetSandboxId,
                        ["target_gateway_endpoint"] = runtimeContext.TargetGatewayEndpoint,
                        ["target_http_base_url"] = runtimeContext.TargetHttpBaseUrl,
                        ["runtime_context_copy_hint"] = $"mkdir -p /workspace/runtime && cp <attached-runtime-context> {runtimeContext.RuntimeContextDefaultPath}",
                        ["materials_attached"] = runtimeContext.MaterialsAttached ? "true" : "false"
                    },
                    Materials =
                    [
                        new HiringConversationMaterialDto
                        {
                            Type = "runtime-context-json",
                            Name = "evaluation-context.json",
                            Content = runtimeContext.RuntimeContextJson,
                            MimeType = "application/json"
                        }
                    ]
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

        if (HasMaterialsSupplementPrompt(timelineResult.Data.Messages) &&
            HasTargetSandboxContextPrompt(timelineResult.Data.Messages))
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
                Content = "In your next response, first output exactly `目标沙箱连接上下文已就绪。` Then explain that the target sandbox connection metadata is already available in this conversation and you can use the attached runtime_context.json plus internal auth logic to connect to the target sandbox without asking the user for endpoint or token. After that, tell the user which evaluation materials are missing and ask for testcase/ontology supplements before continuing.",
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["missing_materials"] = BuildMissingMaterialsSummary(testcaseReady, ontologyReady),
                    ["next_step"] = "Ask the user to upload scenario materials or provide scenario description, then run scenario_parser and retry evaluation.",
                    ["target_context_ready"] = "true",
                    ["runtime_context_json"] = runtimeContext.RuntimeContextJson,
                    ["runtime_context_default_path"] = runtimeContext.RuntimeContextDefaultPath,
                    ["target_sandbox_id"] = workspaceContext.TargetSandboxId,
                    ["target_gateway_endpoint"] = runtimeContext.TargetGatewayEndpoint,
                    ["target_http_base_url"] = runtimeContext.TargetHttpBaseUrl,
                    ["runtime_context_copy_hint"] = $"mkdir -p /workspace/runtime && cp <attached-runtime-context> {runtimeContext.RuntimeContextDefaultPath}",
                    ["materials_attached"] = runtimeContext.MaterialsAttached ? "true" : "false"
                },
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "runtime-context-json",
                        Name = "evaluation-context.json",
                        Content = runtimeContext.RuntimeContextJson,
                        MimeType = "application/json"
                    }
                ]
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
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var workspaceKey = BuildWorkspaceKey(owner, employeeId);
        if (EvaluationWorkspaces.TryGetValue(workspaceKey, out var workspaceContext))
        {
            var stepStates = new Dictionary<string, WorkspaceStepState>(workspaceContext.StepStates, StringComparer.OrdinalIgnoreCase)
            {
                ["materials"] = new("running", "Inspecting testcases and ontology materials")
            };
            EvaluationWorkspaces[workspaceKey] = workspaceContext with { StepStates = stepStates };
        }

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

        var readiness = BuildReadiness(testcaseReady, ontologyReady);

        if (EvaluationWorkspaces.TryGetValue(workspaceKey, out workspaceContext))
        {
            var detail = readiness.Status.Equals("ready", StringComparison.OrdinalIgnoreCase)
                ? "Testcases and ontology are ready"
                : readiness.Message;
            var stepStates = new Dictionary<string, WorkspaceStepState>(workspaceContext.StepStates, StringComparer.OrdinalIgnoreCase)
            {
                ["materials"] = new("completed", detail)
            };
            EvaluationWorkspaces[workspaceKey] = workspaceContext with { StepStates = stepStates };
        }

        return readiness;
    }

    private async Task<ApiResponse<string>> PrepareEvaluatorMaterialsArchiveAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        CancellationToken cancellationToken)
    {
        var testcaseAssets = await GetLatestSessionAssetsAsync(
            sessionEntity.Id,
            "testcases-json",
            cancellationToken);
        var ontologyAsset = await GetLatestSessionAssetAsync(
            sessionEntity.Id,
            "ontology-json",
            cancellationToken);

        if (testcaseAssets.Count == 0 || ontologyAsset is null)
        {
            var readiness = await PrimeReadinessMaterialsAsync(owner, employee.EmployeeId, cancellationToken);
            if (!readiness.Status.Equals("ready", StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<string>.ErrorResponse(
                    422,
                    string.IsNullOrWhiteSpace(readiness.Message)
                        ? "evaluation materials are not ready"
                        : readiness.Message);
            }

            testcaseAssets = await GetLatestSessionAssetsAsync(
                sessionEntity.Id,
                "testcases-json",
                cancellationToken);
            ontologyAsset = await GetLatestSessionAssetAsync(
                sessionEntity.Id,
                "ontology-json",
                cancellationToken);
        }

        if (testcaseAssets.Count == 0)
        {
            return ApiResponse<string>.ErrorResponse(422, "no testcase assets found for auto evaluation");
        }

        if (ontologyAsset is null)
        {
            return ApiResponse<string>.ErrorResponse(422, "no ontology asset found for auto evaluation");
        }

        var testcaseFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var testcaseAsset in testcaseAssets)
        {
            var content = await ReadEvaluationAssetTextAsync(testcaseAsset, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning(
                    "[Eval] Skip empty testcase asset when preparing evaluator materials. SessionId={SessionId}, RelativePath={RelativePath}",
                    sessionEntity.SessionId,
                    testcaseAsset.RelativePath);
                continue;
            }

            var fileName = ExtractEvaluationAssetFileName(testcaseAsset.RelativePath, "evaluation-testcases.json");
            testcaseFiles[fileName] = content;
        }

        if (testcaseFiles.Count == 0)
        {
            return ApiResponse<string>.ErrorResponse(422, "testcase assets exist but none could be read for auto evaluation");
        }

        var ontologyContent = await ReadEvaluationAssetTextAsync(ontologyAsset, cancellationToken);
        if (string.IsNullOrWhiteSpace(ontologyContent))
        {
            return ApiResponse<string>.ErrorResponse(422, "ontology asset exists but could not be read for auto evaluation");
        }

        var archiveBytes = BuildEvaluatorMaterialsArchive(
            testcaseFiles,
            ExtractEvaluationAssetFileName(ontologyAsset.RelativePath, "evaluation-ontology.json"),
            ontologyContent);

        var uploadResult = await sandboxService.UploadAttachmentAsync(
            new SandboxAttachmentUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Managed,
                ScopeKey = workspaceContext.EvaluatorHireId,
                SandboxRole = "evaluation-evaluator",
                OwnerSubject = owner,
                TenantId = "tenant-default",
                OperatorId = "operator-default",
                SandboxId = workspaceContext.EvaluatorSandboxId,
                Material = new HiringConversationMaterialDto
                {
                    Type = "evaluation-materials-zip",
                    Name = "evaluation-materials.zip",
                    Content = Convert.ToBase64String(archiveBytes),
                    MimeType = "application/zip",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["contentEncoding"] = "base64"
                    }
                }
            },
            cancellationToken);
        if (!uploadResult.Success || uploadResult.Data is null)
        {
            return ApiResponse<string>.ErrorResponse(
                uploadResult.Code,
                $"failed to upload evaluator materials archive: {uploadResult.Message}");
        }

        return ApiResponse<string>.SuccessResponse(ResolveMediaCachePathFromAttachment(uploadResult.Data));
    }

    private async Task<IReadOnlyList<EvaluationAssetEntity>> GetLatestSessionAssetsAsync(
        Guid sessionEntityId,
        string assetType,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.EvaluationAssets
            .AsNoTracking()
            .Where(item =>
                item.SessionEntityId == sessionEntityId &&
                item.AssetType == assetType)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return candidates
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.RelatedKey) ? item.RelativePath : item.RelatedKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.CreatedAtUtc)
                .First())
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<EvaluationAssetEntity?> GetLatestSessionAssetAsync(
        Guid sessionEntityId,
        string assetType,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvaluationAssets
            .AsNoTracking()
            .Where(item =>
                item.SessionEntityId == sessionEntityId &&
                item.AssetType == assetType)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> ReadEvaluationAssetTextAsync(
        EvaluationAssetEntity asset,
        CancellationToken cancellationToken)
    {
        var physicalPath = ResolvePhysicalAssetPath(asset.RelativePath);
        if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath))
        {
            logger.LogWarning(
                "[Eval] Evaluation asset file not found when preparing evaluator materials. RelativePath={RelativePath}",
                asset.RelativePath);
            return null;
        }

        return await File.ReadAllTextAsync(physicalPath, cancellationToken);
    }

    internal static byte[] BuildEvaluatorMaterialsArchive(
        IReadOnlyDictionary<string, string> testcaseFiles,
        string ontologyFileName,
        string ontologyContent)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var testcaseFile in testcaseFiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = archive.CreateEntry(
                    $"testcases/{BuildSafeArchiveFileName(testcaseFile.Key, "evaluation-testcases.json")}",
                    CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                var contentBytes = Encoding.UTF8.GetBytes(testcaseFile.Value);
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }

            var ontologyEntry = archive.CreateEntry(
                $"ontology/{BuildSafeArchiveFileName(ontologyFileName, "evaluation-ontology.json")}",
                CompressionLevel.Fastest);
            using var ontologyStream = ontologyEntry.Open();
            var ontologyBytes = Encoding.UTF8.GetBytes(ontologyContent);
            ontologyStream.Write(ontologyBytes, 0, ontologyBytes.Length);
        }

        return memoryStream.ToArray();
    }

    private static string ExtractEvaluationAssetFileName(string? relativePath, string fallbackFileName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return fallbackFileName;
        }

        var normalizedPath = relativePath.Replace('\\', '/').Trim();
        var fileName = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(fileName)
            ? fallbackFileName
            : fileName;
    }

    private static string BuildSafeArchiveFileName(string? fileName, string fallbackFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName)
            ? fallbackFileName
            : Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = fallbackFileName;
        }

        var safeChars = candidate
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_')
            .ToArray();
        var safeName = new string(safeChars).Trim('.');
        return string.IsNullOrWhiteSpace(safeName)
            ? fallbackFileName
            : safeName;
    }

    private string BuildRuntimeContextJson(
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        string targetGatewayEndpoint,
        string? explicitMaterialsPath)
    {
        var runtimeContext = new
        {
            session = new
            {
                session_id = sessionEntity.SessionId,
                employee_id = employee.EmployeeId,
                employee_name = employee.Nickname,
                iteration = sessionEntity.Iteration
            },
            materials = new
            {
                workspace_root = "/workspace",
                template_root = "/workspace",
                template_package_zip = workspaceContext.EvaluatorTemplatePackageZipPath,
                testcases_path = explicitMaterialsPath,
                ontology_path = explicitMaterialsPath
            },
            target_sandbox = new
            {
                sandbox_id = workspaceContext.TargetSandboxId,
                ws_endpoint = targetGatewayEndpoint,
                gateway_endpoint = targetGatewayEndpoint,
                http_base_url = ResolveHttpBaseUrl(targetGatewayEndpoint)
            },
            execution = new
            {
                timeout_seconds = 120,
                http_supplement = true
            }
        };

        logger.LogInformation(
            "[Eval] Runtime context built employeeId={EmployeeId} sessionId={SessionId} targetSandboxId={TargetSandboxId} targetGatewayEndpoint={TargetGatewayEndpoint} materialsPath={MaterialsPath}",
            employee.EmployeeId,
            sessionEntity.SessionId,
            workspaceContext.TargetSandboxId,
            targetGatewayEndpoint,
            explicitMaterialsPath ?? string.Empty);

        return JsonSerializer.Serialize(runtimeContext, JsonOptions);
    }

    private string BuildLiveEvaluationBootstrapPayload(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        string targetGatewayEndpoint,
        string runtimeContextPath)
    {
        var bootstrapPayload = new
        {
            workflow = "live_evaluation",
            owner_subject = owner,
            session_id = sessionEntity.SessionId,
            evaluator_sandbox_id = workspaceContext.EvaluatorSandboxId,
            target_hire_id = workspaceContext.TargetHireId,
            target_sandbox_id = workspaceContext.TargetSandboxId,
            runtime_context_path = runtimeContextPath,
            instruction = $$"""
                          BOOTSTRAP MODE. Execute all steps without outputting any text. Your entire response must be ONLY the final verdict JSON — no prefix, no suffix, no markdown fences, no explanation.

                          1) cp "{{runtimeContextPath}}" /workspace/runtime/evaluation-context.json

                          2) python /workspace/skills/live_evaluator/evaluate.py --runtime-context /workspace/runtime/evaluation-context.json --mode inspect --output /tmp/materials_inspection.json

                          3) If inspect status != ready, output exactly: {"verdict":"FAIL","overall_score":0,"summary":"materials incomplete","dimension_scores":[]}
                             Then STOP.

                          4) python /workspace/skills/live_evaluator/evaluate.py --runtime-context /workspace/runtime/evaluation-context.json --mode execute --output /tmp/trace_result.json

                          5) Read /tmp/trace_result.json. If "status" is not "completed" or "turns" is empty, run the following command and output its stdout only, then STOP:
                             python - <<'PY'
                             import json
                             from pathlib import Path

                             trace = json.loads(Path('/tmp/trace_result.json').read_text(encoding='utf-8'))
                             status = str(trace.get('status') or 'unknown').strip() or 'unknown'
                             meta = trace.get('meta') or {}
                             error = (
                                 trace.get('error')
                                 or trace.get('message')
                                 or (meta.get('error') if isinstance(meta, dict) else None)
                                 or ''
                             )
                             error_text = str(error).replace('\r', ' ').replace('\n', ' ').replace('{', '(').replace('}', ')').strip()
                             if not trace.get('turns'):
                                 error_text = f"{error_text}; turns empty".strip('; ')
                             if not error_text:
                                 error_text = 'unknown'

                             summary = f"execution error: status={status}; error={error_text}"
                             print(json.dumps({
                                 "verdict": "FAIL",
                                 "overall_score": 0,
                                 "summary": summary,
                                 "dimension_scores": []
                             }, ensure_ascii=False))
                             PY

                          6) Output the verdict. No braces in summary. Entire response must be ONLY:
                          {"verdict":"PASS|FAIL","overall_score":0-100,"summary":"...","dimension_scores":[{"dimension":"accuracy|completeness|compliance|communication","score":0-100,"comment":"...","evidence_refs":[]}]}
                          """
        };

        return JsonSerializer.Serialize(bootstrapPayload, JsonOptions);
    }

    private static string ResolveHttpBaseUrl(string gatewayEndpoint)
    {
        if (string.IsNullOrWhiteSpace(gatewayEndpoint))
        {
            return string.Empty;
        }

        var endpoint = gatewayEndpoint.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return endpoint;
        }

        var scheme = uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase)
                ? "http"
                : uri.Scheme;

        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = string.Empty,
            Query = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority);
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
                            ? (passed ? "Evaluator sandbox verdict: pass." : "Evaluator sandbox verdict: fail.")
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

