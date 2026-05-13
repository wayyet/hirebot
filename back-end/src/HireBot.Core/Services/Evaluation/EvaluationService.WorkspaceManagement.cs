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


        // 濠电偞鍨堕幐鍝ョ矓閹绢啟鍥蓟閵夛箑浠洪梺闈涱焾閸庨亶宕甸幒妤佺厸闁割偅绻嶅Σ绋棵归崗鐓庡闁瑰嘲鎳庨…銊╁礃閵娾晜顔?闂?婵犳鍣徊鐣屾崲閹达富鏁冨┑鍌氭啞閸嬨劑鏌曟繝蹇曠暠闁绘挻娲熼弻锝夊箛椤旇棄娈岄梺?闂備礁鎲￠崹闈浳涘Δ鍚藉洭顢楅崒娑樼彴闂佸憡娲︽禍婊冾嚕閻戣姤鐓熸繝濠傞閻忕姵銇?闂傚倷绶￠崰鏇犲垝濞嗘挸闂柟闂寸濡﹢鏌℃径濠勪虎闁诲骏绱曢埀?

        // 闂備礁鎲＄敮妤冩崲閸岀儑缍栭柟鐗堟緲缁€宀勬煛瀹ュ海鍘涢柛鏂垮铻栭柣姗嗗枛閳ь剚顨嗛幈銊╁箹娴ｅ摜鐣辨繛杈剧到閹芥粓骞楅悢鍏肩厱?闂?闂?EvaluationWorkspaces 濠电偞鍨堕幖鈺呭储閸婄喆浜瑰〒姘ｅ亾鐎规洘绮岄濂稿炊閳轰緡妲遍梺璇插缁嬫帡鏁冮銈嗩潟婵犻潧娲︽刊鎾煠閹颁礁鐏ｉ柡瀣€块幃褰掑炊閵夈儳浼勫銈呮禋閸撶喖骞冨▎鎾村仭闁哄鍎婚澶愭煟?
        var stepStates = new Dictionary<string, WorkspaceStepState>(StringComparer.OrdinalIgnoreCase)
        {
            ["target_sandbox"] = new("running", null),
            ["evaluator_sandbox"] = new("pending", null),
            ["upload_skill"] = new("pending", null),
            ["upload_employee_template"] = new("pending", null),
            ["upload_artifacts"] = new("pending", null),
            ["materials"] = new("pending", null)
        };

        // 闂備礁婀辩划顖炲礉閺囩喐娅犻柣妯款嚙缁€鍐╃箾閸℃绠扮€殿喖纾槐鎾诲磼濞戞瑥纰嶉梺瀹︽澘濮傞柡浣哥Ф娴狅箓鎮℃惔妯荤€?workspace-status 闂佸搫顦遍崕鎰板窗濞戙埄鏁嬫俊銈呮噺閸ゅ嫰鏌﹀Ο渚▓婵＄虎鍠氱槐鎺楀棘濞嗘儳鍓伴梺绯曟杺閸ㄤ粙骞嗛崶鈹惧亾閿濆簼绨婚柣鎾冲€垮鍫曞煛閸屾壕妲堥柣?
        var placeholder = new EvaluationWorkspaceContext(
            TargetHireId: string.Empty,
            TargetSandboxId: string.Empty,
            EvaluatorHireId: string.Empty,
            EvaluatorSandboxId: string.Empty,
            SkillLoadedAtUtc: null,
            SessionId: null,
            EvaluatorTemplatePackageZipPath: null,
            StepStates: stepStates);
        EvaluationWorkspaces[workspaceKey] = placeholder;

        // Create target sandbox directly via native sandbox API
        var targetResult = await CreateEvaluationSandboxAsync(owner, employeeId, "evaluation-target", cancellationToken);
        if (!targetResult.Success || targetResult.Data.SandboxId is null)
        {
            stepStates["target_sandbox"] = new("failed", targetResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(targetResult.Code, targetResult.Message);
        }

        var (targetRuntimeId, targetSandboxId) = targetResult.Data;
        stepStates["target_sandbox"] = new("completed", targetSandboxId);
        stepStates["evaluator_sandbox"] = new("running", null);

        // Create evaluator sandbox directly via native sandbox API
        var evaluatorResult = await CreateEvaluationSandboxAsync(owner, employeeId, "evaluation-evaluator", cancellationToken);
        if (!evaluatorResult.Success || evaluatorResult.Data.SandboxId is null)
        {
            stepStates["evaluator_sandbox"] = new("failed", evaluatorResult.Message);
            return ApiResponse<EvaluationWorkspaceContext>.ErrorResponse(evaluatorResult.Code, evaluatorResult.Message);
        }

        var (evaluatorRuntimeId, evaluatorSandboxId) = evaluatorResult.Data;
        stepStates["evaluator_sandbox"] = new("completed", evaluatorSandboxId);

        var workspaceContext = new EvaluationWorkspaceContext(
            TargetHireId: targetRuntimeId,
            TargetSandboxId: targetSandboxId,
            EvaluatorHireId: evaluatorRuntimeId,
            EvaluatorSandboxId: evaluatorSandboxId,
            SkillLoadedAtUtc: null,
            SessionId: null,
            EvaluatorTemplatePackageZipPath: null,
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
            EvaluatorTemplatePackageZipPath = employeeTemplateResult.Data
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

        var evaluatorArtifactUploadResult = await UploadArtifactToSandboxAsync(
            evaluatorSandboxId,
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
        byte[] archiveBytes;
        string fileName;

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
    }

    /// <summary>
    /// 闂佽绻愮换鎰涘☉妯忕儤瀵奸弶鎴濆敤闂佹悶鍎滈崟顐ｇ€柣搴ゎ潐閹爼宕曢崘娴嬫灁闁硅揪绠戦弸渚€鏌℃径搴㈢《闁圭晫鍠栭弻娑樷槈濞咁収浜濈€靛ジ骞囬鈺冨枛閸╁嫰宕橀埡鍐ㄥ殥濠电偞鍨堕幐鎼佹晝閿濆洦顫曢柛顐ｆ礀缁€鍡涙煙濞堝灝鏋熺紒鎰殜閺屸€愁吋閸涱喖顦╅梺娲诲幗閻熝呭垝婵犳艾鐭楁俊顖濆亹閹插潡鏌ｉ悩鍙夋悙閻庢凹浜畷锝嗙節閸屾鐓㈠┑鐐叉閸╁牓宕幖浣圭厪?
    /// 闂備胶鍎甸弲鈺呭窗濡ゅ懏鍋夐柨婵嗘噳閺岋附绻涢崱妯虹劸闁哥偞鎮傚濠氬炊閿濆懍澹曢梺鑽ゅ枑濞叉垿鎮為敃浣告殲闂備礁鎼ˇ鎵偓绗涘喚鐒介柣銏㈩焾缁犮儳鎲搁幋锔衡偓渚€骞嬮悙纰樻灃濠殿喗锕╅崜娆擄綖閵堝鈷戞い鎰剁稻椤绱掓０婵嗕喊闁轰礁绉撮悾婵嬪礃椤忓拋娼犲┑鐐殿棎閸嬫劖鏅跺Δ鍛剹婵炲棙鍨规稉宥夋⒑椤掆偓缁夊灚绂掑鈧幃鐑藉即濮樺崬濡介柤鍨涙櫊閺屸剝寰勭€ｎ亶鍤嬪┑鐐靛帶閻忔氨绮嬪澶婂耿婵絾瀵х敮鈥崇暦閵娿儙鐔告姜閹殿喚鐓戦梻浣哄帶閻ゅ洤螞閸曨剚鍙忛煫鍥ㄧ☉杩?
    /// </summary>
    private async Task<ApiResponse<string?>> UploadEmployeeTemplateToSandboxAsync(
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
            return ApiResponse<string?>.SuccessResponse(null, "employee template upload skipped: missing SourceTemplateId");
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
                return ApiResponse<string?>.ErrorResponse(404, $"fixture template package not found for templateId: {templateId}");
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
                return ApiResponse<string?>.ErrorResponse(422, $"failed to load fixture template package: {templateId}");
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
                return ApiResponse<string?>.ErrorResponse(502, $"failed to load template package: {templateId}");
            }
        }

        if (templatePackage.PackageFiles.Count == 0)
        {
            logger.LogWarning("[Eval] Template package {TemplateId} has no files", templateId);
            return ApiResponse<string?>.SuccessResponse(null, "employee template upload skipped: package has no files");
        }

        var archiveBytes = EmployeeHiringService.BuildDigitalEmployeeArchive(templatePackage);
        if (archiveBytes.Length == 0)
        {
            logger.LogError("[Eval] Template archive is empty for templateId={TemplateId}", templateId);
            return ApiResponse<string?>.ErrorResponse(422, "employee template archive is empty");
        }

        var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";

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
            return ApiResponse<string?>.ErrorResponse(targetUploadResult.Code, $"failed to upload employee template to target sandbox: {targetUploadResult.Message}");
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
            return ApiResponse<string?>.ErrorResponse(evaluatorUploadResult.Code, $"failed to upload employee template attachment to evaluator sandbox: {evaluatorUploadResult.Message}");
        }

        var templatePackageZipPath = ResolveMediaCachePathFromAttachment(evaluatorUploadResult.Data);
        logger.LogInformation(
            "[Eval] Employee template uploaded to evaluator sandbox attachment sandboxId={SandboxId} mediaId={MediaId} marker={Marker} templatePackageZipPath={TemplatePackageZipPath}",
            evaluatorSandboxId,
            evaluatorUploadResult.Data.MediaId,
            evaluatorUploadResult.Data.Marker,
            templatePackageZipPath);

        return ApiResponse<string?>.SuccessResponse(templatePackageZipPath, "employee template uploaded");
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
                    Content = "Evaluation materials are ready. Continue with question cards, scoring rules, or start execution.",
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
                Content = "Evaluation materials are incomplete. Ask the user to provide missing testcase/ontology files, then continue.",
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["missing_materials"] = BuildMissingMaterialsSummary(testcaseReady, ontologyReady),
                    ["next_step"] = "Ask the user to upload scenario materials or provide scenario description, then run scenario_parser and retry evaluation."
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

    private string BuildLiveEvaluationBootstrapPayload(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        string targetGatewayEndpoint,
        string sandboxAccessToken)
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
                testcases_path = (string?)null,
                ontology_path = (string?)null
            },
            target_sandbox = new
            {
                sandbox_id = workspaceContext.TargetSandboxId,
                ws_endpoint = targetGatewayEndpoint,
                gateway_endpoint = targetGatewayEndpoint,
                http_base_url = ResolveHttpBaseUrl(targetGatewayEndpoint),
                auth = BuildTargetSandboxAuthContext(sandboxAccessToken)
            },
            execution = new
            {
                timeout_seconds = 120,
                http_supplement = true
            }
        };

        var bootstrapPayload = new
        {
            workflow = "live_evaluation",
            owner_subject = owner,
            session_id = sessionEntity.SessionId,
            evaluator_sandbox_id = workspaceContext.EvaluatorSandboxId,
            target_hire_id = workspaceContext.TargetHireId,
            target_sandbox_id = workspaceContext.TargetSandboxId,
            instruction = """
                          濠电偠鎻徊鎸庢叏閸撗勫床鐎广儱娲﹂崑姗€鎮橀悙璺盒撻棅顒夊墴閹綊宕惰椤徰囨煕濞嗗骏宸ラ柍鏄忔閳诲酣骞嬮鐐存毈闂備焦瀵х粙鎴︽儗娓氣偓椤㈡岸顢楅崟顐ゎ唶婵犮垼娉涢ˇ顔捐姳閺夊簱妲堥柟鐐墯閸庢棃鏌曢崱妤€顒㈤柟顖涙缁犳盯寮惔鎾村瘱闂佽崵鍋炵粙鎴︽儗婢跺本顫?
                          1) 闂?runtime_context 闂備礁鎲￠…鍥窗閹扮増鍋嬮梺顒€绉寸粈鍐╃箾閸℃绠扮€?/workspace/runtime/evaluation-context.json闂備焦瀵х粙鎴濓耿缁傘€?8闂?
                          2) 闂備礁婀遍悷鎶藉幢閳哄倹鏉?inspect闂?
                             python /workspace/skills/live_evaluator/evaluate.py --runtime-context /workspace/runtime/evaluation-context.json --mode inspect --output /tmp/materials_inspection.json
                          3) 闂?inspect 闂佸搫顦弲婊堝蓟閵娿儍?materials_incomplete闂備焦瀵х粙鎴﹀嫉椤掆偓鍗遍柟瀵稿У閸忔粍銇勯弬鍨倯闁哥喓鍋ら弻锝夘敇濠婂啫濮㈤悗瑙勭摃妞寸顕ラ崟顖涚劶鐎广儱鍟犻崑鎾愁吋婢跺苯绁﹂柣鐘荤細濞咃絿绮氶崸妤佸€靛ù锝呭暙娴滃綊鏌℃担闈涒偓婵嬬嵁鐎ｎ喗鍋い鏍ゅ亾濠㈣埖鍔曠粈鍌炴煕濞戝崬鏋熺紓宥呯箻閺岋繝宕掑☉姘櫑闂佽鍠楅〃濠囧蓟鐏炵瓔鍚嬮柛顐犲灪绗?
                          4) 闂備礁鍚嬮惇褰掑磿閹绘帩鐒芥俊銈呮噹濡ɑ绻涢崱妤冪闁汇劍鍨圭槐鎾寸瑹閸ワ附鍊ｇ紓浣介哺缁诲牆鐣峰Δ鍛唶婵犻潧鐗婄紞宀€绱撴担鎻掍壕?question_cards闂備焦瀵х粙鎴︽嚐椤栫偛鐤柍褜鍓熼弻鐔虹箔濞戞ɑ锛嶉柡鈧?execute闂?
                             python /workspace/skills/live_evaluator/evaluate.py --runtime-context /workspace/runtime/evaluation-context.json --mode execute --output /tmp/trace_result.json
                          5) 闂備胶纭堕弲鐐差浖閵娧嗗С?trace 濠?ontology 闂佸搫顦弲婊呯矙閺嶎厹鈧線骞嬮悩鍐茬彴闂佸憡娲﹂崑鍕倵婵犳碍鐓ユ繛鎴烆焽閻掗绱掓０婵嗗籍鐎规洘鐟╅幃婊兾熺拋宕囧笡缂?verdict JSON闂備焦瀵х粙鎴︽偋閸℃瑦宕查柍褜鍓欓—鍐Χ閸偄娈悷婊勫鐏忔瑩骞夐幘顔芥櫜闁糕剝鐟㈤崑鎾寸鐎ｎ偅娅栨繝銏ｅ煐缁嬫挾绮?
                             {
                               "verdict": "PASS|FAIL",
                               "overall_score": 0-100,
                               "summary": "string",
                               "dimension_scores": [
                                 {"dimension":"accuracy|completeness|compliance|communication","score":0-100,"comment":"string","evidence_refs":["..."]}
                               ]
                             }
                          """,
            runtime_context = runtimeContext
        };

        return JsonSerializer.Serialize(bootstrapPayload, JsonOptions);
    }

    private object BuildTargetSandboxAuthContext(string sandboxAccessToken)
    {
        return new
        {
            mode = "static_token",
            access_token = sandboxAccessToken.Trim(),
            ws_transport = "query",
            ws_query_param = "token",
            http_header_name = "Authorization",
            http_scheme = "Bearer"
        };
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

