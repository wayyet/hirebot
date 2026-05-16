using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Migration;
using HireBot.Abstraction.Models.Team;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using Microsoft.Extensions.Hosting;
using System.IO.Compression;
using System.Text.Json;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 员工运行时服务，管理员工实例的生命周期、状态流转和配置。
/// </summary>
public sealed partial class EmployeeRuntimeService(
    IEmployeeRuntimeStore store,
    ITeamImProvider teamImProvider,
    ICollaborationService collaborationService,
    IRequestContextService requestContextService,
    HireBotDbContext dbContext,
    IInstanceArtifactCloneService artifactCloneService,
    IInstanceArtifactResolver instanceArtifactResolver,
    ISandboxService sandboxService,
    IKingCrabHttpClient kingCrabHttpClient,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment) : IEmployeeRuntimeService
{
    /// <summary>
    /// 支持的员工状态列表。
    /// </summary>
    private static readonly HashSet<string> SupportedStatuses =
    [
        "hired",
        "interning_ai",
        "interning_human",
        "live",
        "failed",
        "retired"
    ];

    /// <summary>
    /// 允许的状态流转映射。
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedStatusTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["hired"] = ["interning_ai", "failed", "retired"],
            ["interning_ai"] = ["interning_human", "failed", "retired"],
            ["interning_human"] = ["live", "failed", "retired"],
            ["live"] = ["retired"],
            ["failed"] = ["hired", "interning_ai", "interning_human", "retired"],
            ["retired"] = []
        };

    /// <summary>
    /// Fixture 状态种子顺序。
    /// </summary>
    private static readonly string[] FixtureStatusSeedOrder =
    [
        "hired",
        "interning_ai",
        "interning_human",
        "live"
    ];

    /// <summary>
    /// Fixture 模板绑定配置。
    /// </summary>
    private static readonly Lazy<IReadOnlyDictionary<string, FixtureTemplateBinding>> FixtureTemplateBindings =
        new(LoadFixtureTemplateBindings);

    private const string RuntimeSandboxRole = "runtime";
    private const int DefaultMaxActivePersonalClonesPerOwner = 10;

    /// <summary>
    /// Fixture 模板绑定记录。
    /// </summary>
    private sealed record FixtureTemplateBinding(
        string TemplateId,
        string? FixtureTemplateId,
        string? FixtureEmployeeId);

    /// <summary>
    /// 获取员工列表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>员工摘要列表</returns>
    public async Task<ApiResponse<IReadOnlyList<EmployeeSummaryDto>>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        var employees = await ResolveOwnerEmployeesAsync(owner, cancellationToken);
        var summaries = employees.Select(ToSummary).ToArray();

        return ApiResponse<IReadOnlyList<EmployeeSummaryDto>>.SuccessResponse(summaries);
    }

    /// <summary>
    /// 获取当前租户下所有部门数字员工列表。
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<EmployeeSummaryDto>>> GetDepartmentEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var (tenantId, _) = requestContextService.ResolveTenantAndOperator(null, null);
        var query = dbContext.Instances
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.InstanceType == "department")
            .OrderByDescending(item => item.UpdatedAt);
        var employees = await LoadInstancesAsEmployeesAsync(query, cancellationToken: cancellationToken);
        var summaries = employees.Select(ToSummary).ToArray();
        return ApiResponse<IReadOnlyList<EmployeeSummaryDto>>.SuccessResponse(summaries);
    }

    /// <summary>
    /// 获取单个员工详情。
    /// </summary>
    /// <param name="employeeId">员工ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>员工详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> GetEmployeeAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await ResolveEmployeeForOwnerAsync(owner, employeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        return ApiResponse<EmployeeDetailDto>.SuccessResponse(employee);
    }

    /// <summary>
    /// 导入示例实例产物。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>导入结果</returns>
    public async Task<ApiResponse<ImportFixtureInstancesResultDto>> ImportFixtureInstancesAsync(CancellationToken cancellationToken = default)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        var fixtureBundle = await LoadFixtureBundleAsync(owner, cancellationToken);
        if (fixtureBundle.Employees.Count == 0)
        {
            return ApiResponse<ImportFixtureInstancesResultDto>.ErrorResponse(404, "未找到可导入的示例实例产物");
        }

        var importedEmployees = await store.ReplaceOwnerAsync(owner, fixtureBundle.Employees, cancellationToken);
        var importedImItems = await teamImProvider.ReplaceItemsAsync(owner, fixtureBundle.TeamImItems, cancellationToken);
       // await TryUpsertInstanceRecordsAsync(fixtureBundle.Employees, cancellationToken);

        var result = new ImportFixtureInstancesResultDto(
            OwnerSubject: owner,
            FixtureDirectories: fixtureBundle.FixtureDirectories,
            ImportedEmployees: importedEmployees,
            ImportedImItems: importedImItems,
            EmployeeIds: fixtureBundle.Employees.Select(item => item.EmployeeId).ToArray());

        return ApiResponse<ImportFixtureInstancesResultDto>.SuccessResponse(result, "示例实例产物导入完成");
    }

    /// <summary>
    /// 从 Fixture 模板承接员工。
    /// </summary>
    /// <param name="templateId">模板ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>承接结果</returns>
    public async Task<ApiResponse<FixtureTemplateHireResultDto>> HireFromFixtureTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<FixtureTemplateHireResultDto>.ErrorResponse(400, "templateId 不能为空");
        }

        var normalizedTemplateId = templateId.Trim();
        var owner = requestContextService.ResolveOwnerSubject();
        await EnsureSeedDataAsync(owner, cancellationToken);
        var fixtureBinding = ResolveFixtureTemplateBinding(normalizedTemplateId);

        var existingEmployees = await store.ListAsync(owner, cancellationToken);
        var selected = existingEmployees
            .Where(item => string.Equals(item.InstanceType, "department", StringComparison.OrdinalIgnoreCase))
            .Where(item => IsFixtureTemplateMatch(item, normalizedTemplateId, fixtureBinding))
            .Where(IsUploadSkillReadyInstance)
            .OrderBy(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected is not null)
        {
            return ApiResponse<FixtureTemplateHireResultDto>.SuccessResponse(
                new FixtureTemplateHireResultDto(
                    EmployeeId: selected.EmployeeId,
                    TemplateId: normalizedTemplateId,
                    InstanceType: selected.InstanceType,
                    Status: selected.Status,
                    CreatedByFixtureFallback: false),
                "已承接到可评估的 fixture 实例");
        }

        selected = existingEmployees
            .Where(item => string.Equals(item.InstanceType, "department", StringComparison.OrdinalIgnoreCase))
            .Where(item => IsFixtureTemplateMatch(item, normalizedTemplateId, fixtureBinding))
            .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected is not null)
        {
            return ApiResponse<FixtureTemplateHireResultDto>.ErrorResponse(
                409,
                $"fixture 实例当前状态不支持承接（employeeId={selected.EmployeeId}, status={selected.Status}, evalPhase={selected.EvalPhase ?? "null"}）");
        }

        return ApiResponse<FixtureTemplateHireResultDto>.ErrorResponse(
            404,
            $"未找到可承接的 fixture 实例（templateId={normalizedTemplateId}）");
    }

    /// <summary>
    /// 更新员工生命周期状态。
    /// </summary>
    /// <param name="employeeId">员工ID</param>
    /// <param name="request">更新请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的员工详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> UpdateLifecycleAsync(
        string employeeId,
        UpdateEmployeeLifecycleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 与状态信息为必填项");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await ResolveEmployeeForOwnerAsync(owner, employeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        var targetStatus = NormalizeStatus(request.Status, request.LifecycleStatus);
        if (targetStatus is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "status 或 lifecycleStatus 至少传入一个有效值");
        }

        if (!SupportedStatuses.Contains(targetStatus))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, $"不支持的 status：{targetStatus}");
        }

        var currentStatus = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";
        if (!currentStatus.Equals(targetStatus, StringComparison.OrdinalIgnoreCase) &&
            !IsAllowedTransition(currentStatus, targetStatus))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, $"非法状态流转：{currentStatus} -> {targetStatus}");
        }

        var updated = employee with
        {
            Status = targetStatus,
            LifecycleStatus = MapStatusToLifecycleLabel(targetStatus),
            StageSummary = Coalesce(request.StageSummary, employee.StageSummary),
            PrimarySignal = Coalesce(request.PrimarySignal, employee.PrimarySignal),
            SignalLevel = Coalesce(request.SignalLevel, employee.SignalLevel),
            InternshipStartAt = targetStatus == "live"
                ? Coalesce(request.InternshipStartAt, employee.InternshipStartAt, DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"))
                : employee.InternshipStartAt,
            GraduatedAt = targetStatus == "live"
                ? Coalesce(request.GraduatedAt, employee.GraduatedAt, DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"))
                : employee.GraduatedAt
        };

        await store.UpsertAsync(owner, updated, cancellationToken);
        await UpsertInstanceRecordAsync(updated, cancellationToken: cancellationToken);

        if (string.Equals(targetStatus, "retired", StringComparison.OrdinalIgnoreCase))
        {
            await CleanupRetiredInstanceArtifactsAsync(owner, updated.EmployeeId, cancellationToken);
        }
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "状态已更新");
    }

    /// <summary>
    /// 重新雇佣已退役实例，并重新启动运行时沙箱。
    /// </summary>
    /// <param name="employeeId">员工ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重新雇佣后的员工详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> RehireAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await ResolveEmployeeForOwnerAsync(owner, employeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        var status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired";
        if (!string.Equals(status, "retired", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "只有已退役的实例才能重新雇佣");
        }

        if (!string.Equals(employee.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(employee.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "当前实例类型不支持重新雇佣");
        }

        var instance = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == employee.EmployeeId, cancellationToken);
        if (instance is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "实例记录不存在，无法重新雇佣");
        }

        var instanceStatus = NormalizeStatus(instance.Status, null) ?? "hired";
        if (!string.Equals(instanceStatus, "retired", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "实例当前不是退役状态");
        }

        InstanceArtifactResolution artifactResolution;
        try
        {
            artifactResolution = await instanceArtifactResolver.ResolveAsync(instance, cancellationToken);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidOperationException)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, ex.Message);
        }

        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(employee.DepartmentId, employee.OwnerUserId);
        var sandboxSetup = await InitializeRuntimeSandboxAsync(
            employee,
            artifactResolution.ArtifactRoot,
            instance.CurrentVersion,
            owner,
            tenantId,
            operatorId,
            cancellationToken);
        if (!sandboxSetup.Success || sandboxSetup.Data is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(sandboxSetup.Code, sandboxSetup.Message);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var updated = employee with
        {
            Status = "live",
            LifecycleStatus = MapStatusToLifecycleLabel("live"),
            StageSummary = "实例已重新上岗，站内对话可用",
            PrimarySignal = "运行正常",
            SignalLevel = "ok",
            InternshipStartAt = today,
            GraduatedAt = today,
            IsConfigured = true
        };

        await store.UpsertAsync(owner, updated, cancellationToken);
        await UpsertInstanceRecordAsync(updated, currentVersion: instance.CurrentVersion, cancellationToken: cancellationToken);

        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "重新雇佣已完成");
    }

    /// <summary>
    /// 更新员工能力配置。
    /// </summary>
    /// <param name="employeeId">员工ID</param>
    /// <param name="request">更新请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的员工详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> UpdateCapabilitiesAsync(
        string employeeId,
        UpdateEmployeeCapabilitiesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || request.Capabilities.Count == 0)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 与 capabilities 为必填项");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await ResolveEmployeeForOwnerAsync(owner, employeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        var capabilityMap = request.Capabilities
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name.Trim(), item => item.Ready, StringComparer.OrdinalIgnoreCase);

        var merged = employee.Capabilities
            .Select(capability => capabilityMap.TryGetValue(capability.Name, out var ready)
                ? capability with { Ready = ready }
                : capability)
            .ToList();

        foreach (var item in capabilityMap)
        {
            if (merged.All(capability => !capability.Name.Equals(item.Key, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(new EmployeeCapabilityDto(item.Key, item.Value));
            }
        }

        var isConfigured = merged.Count > 0 && merged.All(item => item.Ready);
        var updated = employee with
        {
            Capabilities = merged,
            IsConfigured = isConfigured,
            SignalLevel = isConfigured ? "ok" : "warn",
            PrimarySignal = isConfigured ? "配置已完成，等待启动实习" : $"还有 {merged.Count(item => !item.Ready)} 项能力待配置"
        };

        await store.UpsertAsync(owner, updated, cancellationToken);
        await UpsertInstanceRecordAsync(updated, cancellationToken: cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "能力配置已更新");
    }

    /// <summary>
    /// 完成待办操作。
    /// </summary>
    /// <param name="employeeId">员工ID</param>
    /// <param name="actionId">操作ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的员工详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> CompletePendingActionAsync(
        string employeeId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(actionId))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 与 actionId 为必填项");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await ResolveEmployeeForOwnerAsync(owner, employeeId, cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        var pendingActions = employee.PendingActions.ToList();
        if (int.TryParse(actionId, out var index) && index >= 0 && index < pendingActions.Count)
        {
            pendingActions.RemoveAt(index);
        }
        else
        {
            pendingActions.RemoveAll(item => item.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        }

        var updated = employee with
        {
            PendingActions = pendingActions,
            SignalLevel = pendingActions.Count > 0 ? "warn" : "ok",
            PrimarySignal = pendingActions.Count > 0 ? $"还有 {pendingActions.Count} 项待处理" : "运行正常"
        };

        await store.UpsertAsync(owner, updated, cancellationToken);
        await UpsertInstanceRecordAsync(updated, cancellationToken: cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "待办已处理");
    }

    /// <summary>
    /// 从雇佣记录创建员工。
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的员工详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> CreateFromHireAsync(
        CreateEmployeeFromHireRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.HireId) || string.IsNullOrWhiteSpace(request.TemplateId))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "hire 信息不完整");
        }

        var employee = new EmployeeDetailDto(
            EmployeeId: BuildEmployeeId(),
            Nickname: request.TemplateName,
            RoleName: request.TemplateName,
            SourceTemplate: request.TemplateName,
            SourceTemplateId: request.TemplateId,
            InstanceType: "department",
            Status: "interning_ai",
            BasedOnTemplateId: request.TemplateId,
            FromInstanceId: null,
            OwnerUserId: request.OwnerSubject,
            DepartmentId: string.IsNullOrWhiteSpace(request.TenantId) ? "department-default" : request.TenantId,
            LifecycleStatus: "待实习",
            StageSummary: "交付物已导入，可发起 AI 评估",
            PrimarySignal: "待操作：发起 AI 评估",
            SignalLevel: "ok",
            OwningTeam: request.TenantId,
            CreatedAt: DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            InternshipStartAt: null,
            GraduatedAt: null,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: [],
            Capabilities: request.Capabilities.Select(item => new EmployeeCapabilityDto(item, false)).ToArray(),
            EvalPhase: "pending_materials",
            EvalIteration: 0,
            EvalMaxIterations: 30,
            IsConfigured: false);

        await store.UpsertAsync(request.OwnerSubject, employee, cancellationToken);
        await UpsertInstanceRecordAsync(employee, currentVersion: "v_initial", cancellationToken: cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(employee, "员工实例已创建");
    }

    /// <summary>
    /// 上传模板包并直接从模板创建已上岗员工，跳过雇佣沟通、评估、实习等环节。
    /// </summary>
    public async Task<ApiResponse<EmployeeDetailDto>> QuickCreateFromTemplateAsync(
        Stream zipStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (zipStream is null || zipStream.Length == 0)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "上传的模板包为空");
        }

        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "仅支持 .zip 格式的模板包");
        }

        
        var owner = requestContextService.ResolveOwnerSubject();
        var (tenantId, _) = requestContextService.ResolveTenantAndOperator(null, null);
        await EnsureSeedDataAsync(owner, cancellationToken);

        // 读取 zip 到内存
        byte[] zipBytes;
        using (var ms = new MemoryStream())
        {
            await zipStream.CopyToAsync(ms, cancellationToken);
            zipBytes = ms.ToArray();
        }

        if (zipBytes.Length == 0)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "上传的模板包为空");
        }

        // 解析 manifest.json
        string templateName;
        string templateDisplayName;
        IReadOnlyList<string> skillNames;
        string manifestBasePath;
        Dictionary<string, byte[]> artifactFiles;

        using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            var manifestEntry = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Name, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));

            if (manifestEntry is null)
            {
                return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "模板包中未找到 manifest.json");
            }

            // 解析 manifest 内容
            using var manifestStream = manifestEntry.Open();
            using var doc = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            templateName = "unknown-template";
            templateDisplayName = "unknown-template";

            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var rawName = nameEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(rawName))
                {
                    templateName = rawName;
                    templateDisplayName = rawName;
                }
            }

            if (root.TryGetProperty("display_name", out var displayNameEl) && displayNameEl.ValueKind == JsonValueKind.String)
            {
                var rawDisplay = displayNameEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(rawDisplay))
                {
                    templateDisplayName = rawDisplay;
                }
            }

            // 收集 required skills
            var skills = new List<string>();
            if (root.TryGetProperty("skills", out var skillsArr) && skillsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var skill in skillsArr.EnumerateArray())
                {
                    if (skill.ValueKind != JsonValueKind.Object) continue;

                    var isRequired = true;
                    if (skill.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.False)
                    {
                        isRequired = false;
                    }

                    if (isRequired && skill.TryGetProperty("name", out var skillNameEl) && skillNameEl.ValueKind == JsonValueKind.String)
                    {
                        var skillName = skillNameEl.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(skillName))
                        {
                            skills.Add(skillName);
                        }
                    }
                }
            }

            skillNames = skills;

            // 确定基础路径
            manifestBasePath = "";
            var manifestFullName = manifestEntry.FullName;
            var lastSlash = manifestFullName.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                manifestBasePath = manifestFullName[..(lastSlash + 1)];
            }

            // 收集 artifact 文件（包含 manifest.json）
            artifactFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // 目录

                // 跳过 macOS 元数据
                if (entry.Name.StartsWith("._") || entry.Name == ".DS_Store") continue;
                if (entry.FullName.Contains("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;

                var relativePath = entry.FullName;
                if (manifestBasePath.Length > 0 && relativePath.StartsWith(manifestBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath[manifestBasePath.Length..];
                }

                relativePath = relativePath.TrimStart('/');
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                using var entryStream = entry.Open();
                using var entryMs = new MemoryStream();
                await entryStream.CopyToAsync(entryMs, cancellationToken);
                artifactFiles[relativePath] = entryMs.ToArray();
            }
        }

        // 解析 describe.md
        var describeDocument = ReadDescribeMdFromArtifacts(artifactFiles);
        var cardIntro = describeDocument != null
            ? ExtractCardIntro(describeDocument)
            : null;

        // 生成 employeeId
        var employeeId = BuildEmployeeId();

        // 保存模板文件到 wwwroot/resources/DigitalWorkforce
        var digitalWorkforceRoot = ResolveDigitalWorkforceRoot();
        var digitalWorkforceDir = Path.Combine(digitalWorkforceRoot, employeeId);
        try
        {
            Directory.CreateDirectory(digitalWorkforceDir);
            foreach (var (path, content) in artifactFiles)
            {
                var fullPath = Path.Combine(digitalWorkforceDir, path);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
            }
        }
        catch
        {
            // 文件保存失败不影响员工创建
        }

        // 构造 EmployeeDetailDto — 直接上岗状态
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var capabilities = skillNames.Count > 0
            ? skillNames.Select(skillName => new EmployeeCapabilityDto(skillName, true)).ToArray()
            : [new EmployeeCapabilityDto("站内对话", true)];

        var employeeDto = new EmployeeDetailDto(
            EmployeeId: employeeId,
            Nickname: templateDisplayName,
            RoleName: templateDisplayName,
            SourceTemplate: templateName,
            SourceTemplateId: templateName,
            InstanceType: "department",
            Status: "live",
            BasedOnTemplateId: templateName,
            FromInstanceId: null,
            OwnerUserId: owner,
            DepartmentId: string.IsNullOrWhiteSpace(tenantId) ? "department-default" : tenantId,
            LifecycleStatus: "已上岗",
            StageSummary: describeDocument != null
                ? (ExtractBusinessPositioningOneLiner(describeDocument) ?? "从模板包快速创建，已直接上岗")
                : "从模板包快速创建，已直接上岗",
            PrimarySignal: "运行正常",
            SignalLevel: "ok",
            OwningTeam: tenantId,
            CreatedAt: today,
            InternshipStartAt: today,
            GraduatedAt: today,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: [],
            Capabilities: capabilities,
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: true,
            CardIntro: cardIntro);

        // 数据隔离：按 owner 持久化
        await store.UpsertAsync(owner, employeeDto, cancellationToken);

        // 存储 artifacts（失败不影响员工记录）
        var artifactVersion = "v_initial";
        if (artifactFiles.Count > 0)
        {
            try
            {
                var storedArtifacts = await artifactCloneService.StoreDepartmentArtifactsAsync(employeeId, artifactFiles, cancellationToken);
                artifactVersion = storedArtifacts.CurrentVersion;
            }
            catch
            {
                // artifact 存储失败不阻塞员工创建
            }
        }

        await UpsertInstanceRecordAsync(employeeDto, currentVersion: artifactVersion, describeDocument: describeDocument, cancellationToken: cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(employeeDto, "员工已从模板包创建并直接上岗");
    }

    private static string? ReadDescribeMdFromArtifacts(Dictionary<string, byte[]> artifactFiles)
    {
        var describeKey = artifactFiles.Keys.FirstOrDefault(k =>
            string.Equals(k, "describe.md", StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith("/describe.md", StringComparison.OrdinalIgnoreCase));

        if (describeKey is null)
            return null;

        try
        {
            return Encoding.UTF8.GetString(artifactFiles[describeKey]);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractCardIntro(string describeDocument)
    {
        try
        {
            var blocks = describeDocument.Split("\n---\n");
            string? section1 = null;
            string? section4 = null;

            foreach (var block in blocks)
            {
                var trimmed = block.TrimStart();
                if (trimmed.StartsWith("## 1. "))
                    section1 = block.Trim();
                else if (trimmed.StartsWith("## 4. "))
                    section4 = block.Trim();
            }

            if (section1 is null && section4 is null)
                return null;

            var parts = new List<string>(2);
            if (section1 is not null) parts.Add(section1);
            if (section4 is not null) parts.Add(section4);

            return string.Join("\n\n---\n\n", parts);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractBusinessPositioningOneLiner(string describeDocument)
    {
        try
        {
            var blocks = describeDocument.Split("\n---\n");
            foreach (var block in blocks)
            {
                var trimmed = block.TrimStart();
                if (!trimmed.StartsWith("## 1. "))
                    continue;

                var lines = block.Split('\n');
                foreach (var line in lines)
                {
                    var t = line.Trim();
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (t.StartsWith("##") || t.StartsWith("###")) continue;
                    if (t.StartsWith('|') || t.StartsWith("---")) continue;
                    return t.Replace("**", "");
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveDigitalWorkforceRoot()
    {
        return HireBotPathResolver.ResolveDigitalWorkforceRoot(
            hostEnvironment.ContentRootPath,
            configuration["HireBot:DataRoot"],
            configuration["HireBot:DigitalWorkforceRoot"]);
    }

    /// <summary>
    /// 删除数字员工及其全部关联资源：内存记录、DB 实例、沙箱、IM 配置、五件套 artifact 文件。
    /// 仅允许删除自己创建的数字员工。分身类型（personal_clone / private_branch）要求已退役。
    /// </summary>
    public async Task<ApiResponse<object>> DeleteEmployeeAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return ApiResponse<object>.ErrorResponse(400, "employeeId 不能为空");
        }

        var normalizedId = employeeId.Trim();
        var owner = requestContextService.ResolveOwnerSubject();

        var existing = await store.GetAsync(owner, normalizedId, cancellationToken);
        var instance = await dbContext.Instances
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.InstanceId == normalizedId, cancellationToken);
        var instanceExists = instance is not null;

        if (existing is null && !instanceExists)
        {
            return ApiResponse<object>.ErrorResponse(404, "员工不存在");
        }

        // 分身类型（personal_clone / private_branch）要求已退役，部门员工无此限制
        var isCloneType = instance is not null &&
            (string.Equals(instance.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(instance.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase));
        if (isCloneType)
        {
            var resolvedStatus = instance?.Status
                ?? NormalizeStatus(existing?.Status, existing?.LifecycleStatus);
            if (resolvedStatus is not null &&
                !string.Equals(resolvedStatus, "retired", StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<object>.ErrorResponse(409, "只能删除已退役的数字员工分身");
            }
        }

        if (instance is not null &&
            !string.Equals(instance.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<object>.ErrorResponse(403, "只能删除自己创建的数字员工");
        }

        // 最大努力清理：运行时沙箱 + IM 渠道配置
        await CleanupRetiredInstanceArtifactsAsync(owner, normalizedId, cancellationToken);

        // 1. 从内存 store 移除
        await store.DeleteAsync(owner, normalizedId, cancellationToken);

        // 2. 删除 DB InstanceEntity
        await dbContext.Instances
            .Where(item => item.InstanceId == normalizedId)
            .ExecuteDeleteAsync(cancellationToken);

        // 3. 删除五件套 artifact 目录
        string artifactDir;
        if (instance is not null &&
            (string.Equals(instance.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(instance.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase)))
        {
            var fromId = string.IsNullOrWhiteSpace(instance.FromInstanceId) ? "unknown" : instance.FromInstanceId;
            artifactDir = Path.Combine(
                ResolveArtifactStoreRoot(), "instances", "personal_clone",
                SanitizePathSegment(fromId), normalizedId);
        }
        else
        {
            artifactDir = Path.Combine(ResolveArtifactStoreRoot(), "instances", "department", normalizedId);
        }

        if (Directory.Exists(artifactDir))
        {
            try
            {
                Directory.Delete(artifactDir, recursive: true);
            }
            catch
            {
                // 文件删除失败不阻塞流程
            }
        }

        return ApiResponse<object>.SuccessResponse(new { employeeId = normalizedId }, "员工已删除");
    }

    /// <summary>
    /// 创建个人分身。
    /// </summary>
    /// <param name="sourceEmployeeId">源员工ID</param>
    /// <param name="request">创建请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的分身详情</returns>
    public async Task<ApiResponse<EmployeeDetailDto>> CreatePersonalCloneAsync(
        string sourceEmployeeId,
        CreatePersonalCloneRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceEmployeeId) || request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "sourceEmployeeId 与 displayName 为必填项");
        }

        var normalizedSourceId = sourceEmployeeId.Trim();
        var displayName = request.DisplayName.Trim();
        var owner = requestContextService.ResolveOwnerSubject();
        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(null, null);
        await EnsureSeedDataAsync(owner, cancellationToken);

        var ownerEmployees = await ResolveOwnerEmployeesAsync(owner, cancellationToken);
        var activePersonalCloneCount = ownerEmployees.Count(item =>
            string.Equals(item.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "retired", StringComparison.OrdinalIgnoreCase));
        var maxActivePersonalClones = ResolveMaxActivePersonalClonesPerOwner();
        if (activePersonalCloneCount >= maxActivePersonalClones)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(
                409,
                $"个人分身数量已达上限（最多 {maxActivePersonalClones} 个），请先废弃不再使用的分身。");
        }

        var source = await ResolveDepartmentEmployeeForTenantAsync(tenantId, normalizedSourceId, cancellationToken);
        if (source is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "源部门员工不存在");
        }

        if (!string.Equals(source.InstanceType, "department", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "只能从部门员工创建个人分身");
        }

        if (!string.Equals(source.Status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "只能从已上岗部门员工创建个人分身");
        }

        if (!string.Equals(tenantId, "tenant-default", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source.DepartmentId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(403, "只能复制本部门的部门员工");
        }

        if (await store.ExistsNameAsync(owner, displayName, cancellationToken))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "你已经有同名的分身或私人定制");
        }

        var cloneId = BuildInstanceId("pc");
        InstanceArtifactCloneResult artifactResult;
        try
        {
            artifactResult = await artifactCloneService.CloneArtifactsAsync(source, cloneId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, ex.Message);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var clone = new EmployeeDetailDto(
            EmployeeId: cloneId,
            Nickname: displayName,
            RoleName: source.RoleName,
            SourceTemplate: source.SourceTemplate,
            SourceTemplateId: source.SourceTemplateId,
            InstanceType: "personal_clone",
            Status: "live",
            BasedOnTemplateId: source.BasedOnTemplateId,
            FromInstanceId: source.EmployeeId,
            OwnerUserId: owner,
            DepartmentId: source.DepartmentId,
            LifecycleStatus: MapStatusToLifecycleLabel("live"),
            StageSummary: string.IsNullOrWhiteSpace(request.DisplayDescription)
                ? "个人分身已上岗，站内对话可用"
                : request.DisplayDescription.Trim(),
            PrimarySignal: "运行正常",
            SignalLevel: "ok",
            OwningTeam: source.OwningTeam,
            CreatedAt: today,
            InternshipStartAt: today,
            GraduatedAt: today,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: [],
            Capabilities: source.Capabilities.Select(item => item with { Ready = true }).ToArray(),
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: true);

        var sandboxSetup = await InitializeRuntimeSandboxAsync(
            clone,
            artifactResult.TargetRootPath,
            artifactResult.CurrentVersion,
            owner,
            tenantId,
            operatorId,
            cancellationToken);
        if (!sandboxSetup.Success || sandboxSetup.Data is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(sandboxSetup.Code, sandboxSetup.Message);
        }

        await store.UpsertAsync(owner, clone, cancellationToken);
        await UpsertInstanceRecordAsync(clone, currentVersion: artifactResult.CurrentVersion, cancellationToken: cancellationToken);

        return ApiResponse<EmployeeDetailDto>.SuccessResponse(clone, "个人分身已创建并上岗");
    }

    /// <summary>
    /// 从个人分身创建私有分支。私有分支原地更新原实例，不创建新实例、不创建新沙箱、不切换 IM 路由。
    /// </summary>
    public async Task<ApiResponse<PrivateBranchResultDto>> CreatePrivateBranchAsync(
        string sourceInstanceId,
        CreatePrivateBranchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceInstanceId) || request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(400, "sourceInstanceId 与 displayName 为必填项");
        }

        var normalizedSourceId = sourceInstanceId.Trim();
        var displayName = request.DisplayName.Trim();
        var owner = requestContextService.ResolveOwnerSubject();
        await EnsureSeedDataAsync(owner, cancellationToken);

        var source = await ResolveEmployeeForOwnerAsync(owner, normalizedSourceId, cancellationToken);
        if (source is null)
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(404, "源分身不存在");
        }

        if (!string.Equals(source.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, "只能从个人分身创建私有分支");
        }

        if (!string.Equals(source.Status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, "只能从已上岗的个人分身创建私有分支");
        }

        if (!string.Equals(source.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(403, "只能对自己的分身创建私有分支");
        }

        var ownerEmployees = await ResolveOwnerEmployeesAsync(owner, cancellationToken);
        if (ownerEmployees.Any(item =>
                !string.Equals(item.EmployeeId, normalizedSourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Nickname, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, "你已经有同名的分身或私人定制");
        }

        var sourceEntity = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == normalizedSourceId, cancellationToken);
        if (sourceEntity is null)
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(404, "源分身实例记录不存在");
        }

        try
        {
            await SnapshotPrivateBranchArtifactsAsync(sourceEntity, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException or IOException)
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, ex.Message);
        }

        var stations = request.SelectedStations?.Count > 0
            ? string.Join(",", request.SelectedStations.Select(s => s.Trim().ToLowerInvariant()))
            : "all";

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var branch = source with
        {
            Nickname = displayName,
            InstanceType = "private_branch",
            Status = "live",
            LifecycleStatus = MapStatusToLifecycleLabel("live"),
            StageSummary = string.IsNullOrWhiteSpace(request.DisplayDescription)
                ? $"私有分支已创建，原地更新五件套（工位：{stations}）"
                : request.DisplayDescription.Trim(),
            PrimarySignal = "私人定制已启用，沙箱与 IM 继续沿用原分身",
            SignalLevel = "warn",
            InternshipStartAt = string.IsNullOrWhiteSpace(source.InternshipStartAt) ? today : source.InternshipStartAt,
            GraduatedAt = string.IsNullOrWhiteSpace(source.GraduatedAt) ? today : source.GraduatedAt,
            PendingActions = ["发起 AI 评估", "完成用户自评"],
            IsConfigured = source.IsConfigured
        };

        await store.UpsertAsync(owner, branch, cancellationToken);
        await UpsertInstanceRecordAsync(branch, currentVersion: sourceEntity.CurrentVersion, cancellationToken: cancellationToken);

        return ApiResponse<PrivateBranchResultDto>.SuccessResponse(
            new PrivateBranchResultDto(source.EmployeeId, displayName, "live", source.FromInstanceId ?? string.Empty, false),
            "私有分支已原地启用，请进入评估流程");
    }

    /// <summary>
    /// 废弃私有分支，回滚五件套并将原实例恢复为个人分身。
    /// </summary>
    public async Task<ApiResponse<EmployeeDetailDto>> AbandonPrivateBranchAsync(
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "branchId 不能为空");
        }

        var normalizedBranchId = branchId.Trim();
        var owner = requestContextService.ResolveOwnerSubject();

        var branch = await ResolveEmployeeForOwnerAsync(owner, normalizedBranchId, cancellationToken);
        if (branch is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "私有分支不存在");
        }

        if (!string.Equals(branch.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "只能废弃私有分支类型的实例");
        }

        if (!string.Equals(branch.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(403, "只能废弃自己的私有分支");
        }

        if (string.Equals(branch.Status, "retired", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, "该私有分支已经被废弃");
        }

        var branchEntity = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == normalizedBranchId, cancellationToken);
        if (branchEntity is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "私有分支实例记录不存在");
        }

        try
        {
            await RestorePrivateBranchArtifactsAsync(branchEntity, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException or IOException)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(409, ex.Message);
        }

        var restored = branch with
        {
            InstanceType = "personal_clone",
            Status = "live",
            LifecycleStatus = MapStatusToLifecycleLabel("live"),
            StageSummary = "私有分支已废弃，已恢复为原个人分身",
            PrimarySignal = "已恢复原五件套",
            SignalLevel = "ok",
            PendingActions = [],
            IsConfigured = true
        };
        await store.UpsertAsync(owner, restored, cancellationToken);
        await UpsertInstanceRecordAsync(restored, currentVersion: branchEntity.CurrentVersion, cancellationToken: cancellationToken);

        return ApiResponse<EmployeeDetailDto>.SuccessResponse(restored, "私有分支已废弃，已回滚五件套并恢复为个人分身");
    }

    /// <summary>
    /// 迁移本地状态。
    /// </summary>
    /// <param name="request">迁移请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>迁移结果</returns>
    public async Task<ApiResponse<LocalStateMigrationResultDto>> MigrateLocalStateAsync(
        LocalStateMigrationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ApiResponse<LocalStateMigrationResultDto>.ErrorResponse(400, "请求体不能为空");
        }

        var owner = requestContextService.ResolveOwnerSubject();

        var employees = request.Employees?
            .Where(item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .Select(item => new EmployeeDetailDto(
                item.EmployeeId.Trim(),
                item.Nickname,
                item.RoleName,
                item.SourceTemplate,
                item.SourceTemplateId,
                "department",
                NormalizeStatus(null, item.LifecycleStatus) ?? "hired",
                item.SourceTemplateId,
                null,
                owner,
                string.IsNullOrWhiteSpace(item.OwningTeam) ? "department-default" : item.OwningTeam,
                item.LifecycleStatus,
                item.StageSummary,
                item.PrimarySignal,
                item.SignalLevel,
                item.OwningTeam,
                item.CreatedAt,
                item.InternshipStartAt,
                item.GraduatedAt,
                item.TasksDone,
                item.TasksTotal,
                null,
                item.PendingActions,
                item.CapabilityNames.Select(name => new EmployeeCapabilityDto(name, false)).ToArray(),
                null,
                null,
                null,
                item.IsConfigured))
            .ToArray()
            ?? [];

        var imported = await store.UpsertManyAsync(owner, employees, cancellationToken);
        //await TryUpsertInstanceRecordsAsync(employees, cancellationToken);
        var archivedGroups = request.ArchivedGroupIds?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var archived = await collaborationService.MarkArchivedAsync(archivedGroups, cancellationToken);

        var result = new LocalStateMigrationResultDto(
            ImportedEmployees: imported,
            SkippedEmployees: Math.Max(0, (request.Employees?.Count ?? 0) - imported),
            ArchivedGroups: archived);

        return ApiResponse<LocalStateMigrationResultDto>.SuccessResponse(result, "本地状态迁移完成");
    }

    /// <summary>
    /// Fixture 包记录。
    /// </summary>
    private sealed record FixtureBundle(
        IReadOnlyList<EmployeeDetailDto> Employees,
        IReadOnlyList<TeamImItemDto> TeamImItems,
        int FixtureDirectories);

}
