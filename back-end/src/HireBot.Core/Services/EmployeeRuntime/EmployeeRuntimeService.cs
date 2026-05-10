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
using System.IO.Compression;
using System.Text.Json;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// 员工运行时服务，管理员工实例的生命周期、状态流转和配置。
/// </summary>
public sealed class EmployeeRuntimeService(
    IEmployeeRuntimeStore store,
    ITeamImProvider teamImProvider,
    ICollaborationService collaborationService,
    IRequestContextService requestContextService,
    HireBotDbContext dbContext,
    IInstanceArtifactCloneService artifactCloneService,
    ISandboxService sandboxService,
    IKingCrabHttpClient kingCrabHttpClient) : IEmployeeRuntimeService
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
        await EnsureSeedDataAsync(owner, cancellationToken);
        var employees = await store.ListAsync(owner, cancellationToken);
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
        await EnsureSeedDataAsync(owner, cancellationToken);
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
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
        await TryUpsertInstanceRecordsAsync(fixtureBundle.Employees, cancellationToken);

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
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
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

        // Private branch lifecycle hook: when going live, switch IM routing
        if (string.Equals(targetStatus, "live", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(updated.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(updated.FromInstanceId))
        {
            await SwitchImRoutingToBranchAsync(updated, updated.FromInstanceId, owner, cancellationToken);
        }

        if (string.Equals(targetStatus, "retired", StringComparison.OrdinalIgnoreCase))
        {
            await CleanupRetiredInstanceArtifactsAsync(owner, updated.EmployeeId, cancellationToken);
        }
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "状态已更新");
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
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
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
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
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
            Status: "hired",
            BasedOnTemplateId: request.TemplateId,
            FromInstanceId: null,
            OwnerUserId: request.OwnerSubject,
            DepartmentId: string.IsNullOrWhiteSpace(request.TenantId) ? "department-default" : request.TenantId,
            LifecycleStatus: MapStatusToLifecycleLabel("hired"),
            StageSummary: "实例已生成，等待发起评估",
            PrimarySignal: "待操作：进入 AI 评估",
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

        var source = await store.FindAsync(normalizedSourceId, cancellationToken);
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

        var sandboxSetup = await InitializePersonalCloneSandboxAsync(
            clone,
            artifactResult,
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
    /// 部门长快捷复制：从已上岗部门员工创建新部门员工，跳过评估直接上岗。
    /// </summary>
    public async Task<ApiResponse<QuickCloneResultDto>> QuickCloneAsync(
        string sourceInstanceId,
        QuickCloneRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceInstanceId) || request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(400, "sourceInstanceId 与 displayName 为必填项");
        }

        if (!string.Equals(request.UserRole, "manager", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(403, "仅部门长可执行快捷复制");
        }

        var normalizedSourceId = sourceInstanceId.Trim();
        var displayName = request.DisplayName.Trim();
        var owner = requestContextService.ResolveOwnerSubject();
        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(null, null);
        await EnsureSeedDataAsync(owner, cancellationToken);

        var source = await store.FindAsync(normalizedSourceId, cancellationToken);
        if (source is null)
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(404, "源部门员工不存在");
        }

        if (!string.Equals(source.InstanceType, "department", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(409, "只能从部门员工快捷复制");
        }

        if (!string.Equals(source.Status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(409, "只能从已上岗部门员工快捷复制");
        }

        var allEmployees = await store.ListAsync(owner, cancellationToken);
        var nameConflict = allEmployees.Any(e =>
            string.Equals(e.InstanceType, "department", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Status, "live", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Nickname, displayName, StringComparison.OrdinalIgnoreCase));
        if (nameConflict)
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(409, "该部门内已存在同名的已上岗部门员工");
        }

        var cloneId = BuildInstanceId("qc");
        InstanceArtifactCloneResult artifactResult;
        try
        {
            artifactResult = await artifactCloneService.CloneArtifactsAsync(source, cloneId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(409, ex.Message);
        }

        var sourceInstanceEntity = await dbContext.Instances
            .FirstOrDefaultAsync(i => i.InstanceId == normalizedSourceId, cancellationToken);
        var evalReportId = sourceInstanceEntity?.EvalReportId;

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var clone = new EmployeeDetailDto(
            EmployeeId: cloneId,
            Nickname: displayName,
            RoleName: source.RoleName,
            SourceTemplate: source.SourceTemplate,
            SourceTemplateId: source.SourceTemplateId,
            InstanceType: "department",
            Status: "live",
            BasedOnTemplateId: source.BasedOnTemplateId,
            FromInstanceId: source.EmployeeId,
            OwnerUserId: owner,
            DepartmentId: source.DepartmentId,
            LifecycleStatus: MapStatusToLifecycleLabel("live"),
            StageSummary: string.IsNullOrWhiteSpace(request.DisplayDescription)
                ? "部门员工已上岗（快捷复制），站内对话可用"
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

        var sandboxSetup = await InitializePersonalCloneSandboxAsync(
            clone,
            artifactResult,
            owner,
            tenantId,
            operatorId,
            cancellationToken);
        if (!sandboxSetup.Success || sandboxSetup.Data is null)
        {
            return ApiResponse<QuickCloneResultDto>.ErrorResponse(sandboxSetup.Code, sandboxSetup.Message);
        }

        await store.UpsertAsync(owner, clone, cancellationToken);
        await UpsertInstanceRecordAsync(clone, viaQuickClone: true, currentVersion: artifactResult.CurrentVersion, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(evalReportId))
        {
            var cloneEntity = await dbContext.Instances
                .FirstOrDefaultAsync(i => i.InstanceId == cloneId, cancellationToken);
            if (cloneEntity is not null)
            {
                cloneEntity.EvalReportId = evalReportId;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return ApiResponse<QuickCloneResultDto>.SuccessResponse(
            new QuickCloneResultDto(cloneId, "live", source.EmployeeId, true),
            "部门员工已快捷复制并上岗");
    }

    /// <summary>
    /// 从个人分身创建私有分支。创建后状态为 hired，需经双阶段评估通过后才上岗并切换 IM 路由。
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
        var (tenantId, operatorId) = requestContextService.ResolveTenantAndOperator(null, null);
        await EnsureSeedDataAsync(owner, cancellationToken);

        var source = await store.FindAsync(normalizedSourceId, cancellationToken);
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

        if (await store.ExistsNameAsync(owner, displayName, cancellationToken))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, "你已经有同名的分身或私人定制");
        }

        // Check for nested branch — source cannot itself be a private_branch
        if (string.Equals(source.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, "不能在私有分支上再创建私有分支");
        }

        // Check source doesn't already have an active private branch
        var existingBranch = await dbContext.Instances
            .AnyAsync(item =>
                item.FromInstanceId == normalizedSourceId &&
                item.InstanceType == "private_branch" &&
                item.Status != "retired" &&
                item.OwnerUserId == owner,
                cancellationToken);
        if (existingBranch)
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, "该分身已存在活跃的私有分支，请先废弃旧分支再创建新分支");
        }

        var branchId = BuildInstanceId("pb");
        InstanceArtifactCloneResult artifactResult;
        try
        {
            artifactResult = await artifactCloneService.CloneArtifactsAsync(source, branchId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(409, ex.Message);
        }

        var stations = request.SelectedStations?.Count > 0
            ? string.Join(",", request.SelectedStations.Select(s => s.Trim().ToLowerInvariant()))
            : "all";

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var branch = new EmployeeDetailDto(
            EmployeeId: branchId,
            Nickname: displayName,
            RoleName: source.RoleName,
            SourceTemplate: source.SourceTemplate,
            SourceTemplateId: source.SourceTemplateId,
            InstanceType: "private_branch",
            Status: "hired",
            BasedOnTemplateId: source.BasedOnTemplateId,
            FromInstanceId: source.EmployeeId,
            OwnerUserId: owner,
            DepartmentId: source.DepartmentId,
            LifecycleStatus: MapStatusToLifecycleLabel("hired"),
            StageSummary: string.IsNullOrWhiteSpace(request.DisplayDescription)
                ? $"私有分支已创建，待评估（工位：{stations}）"
                : request.DisplayDescription.Trim(),
            PrimarySignal: "待发起评估",
            SignalLevel: "warn",
            OwningTeam: source.OwningTeam,
            CreatedAt: today,
            InternshipStartAt: null,
            GraduatedAt: null,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: ["发起 AI 评估", "完成用户自评"],
            Capabilities: source.Capabilities.Select(item => item with { Ready = false }).ToArray(),
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: false);

        var sandboxSetup = await InitializePersonalCloneSandboxAsync(
            branch,
            artifactResult,
            owner,
            tenantId,
            operatorId,
            cancellationToken);
        if (!sandboxSetup.Success || sandboxSetup.Data is null)
        {
            return ApiResponse<PrivateBranchResultDto>.ErrorResponse(sandboxSetup.Code, sandboxSetup.Message);
        }

        await store.UpsertAsync(owner, branch, cancellationToken);
        await UpsertInstanceRecordAsync(branch, currentVersion: artifactResult.CurrentVersion, cancellationToken: cancellationToken);

        return ApiResponse<PrivateBranchResultDto>.SuccessResponse(
            new PrivateBranchResultDto(branchId, displayName, "hired", source.EmployeeId, false),
            "私有分支已创建，请进入评估流程");
    }

    /// <summary>
    /// 废弃私有分支。若已上岗则恢复 IM 路由到原分身。
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

        var branch = await store.FindAsync(normalizedBranchId, cancellationToken);
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

        var wasLive = string.Equals(branch.Status, "live", StringComparison.OrdinalIgnoreCase);
        var sourceInstanceId = branch.FromInstanceId;

        // If branch was live, restore IM routing to source clone
        if (wasLive && !string.IsNullOrWhiteSpace(sourceInstanceId))
        {
            await RestoreImRoutingToSourceAsync(sourceInstanceId, normalizedBranchId, owner, cancellationToken);
        }

        // Clean up branch resources
        await CleanupRetiredInstanceArtifactsAsync(owner, normalizedBranchId, cancellationToken);

        // Clear ActiveBranchId on source clone
        if (!string.IsNullOrWhiteSpace(sourceInstanceId))
        {
            var sourceEntity = await dbContext.Instances
                .FirstOrDefaultAsync(item => item.InstanceId == sourceInstanceId, cancellationToken);
            if (sourceEntity is not null && string.Equals(sourceEntity.ActiveBranchId, normalizedBranchId, StringComparison.OrdinalIgnoreCase))
            {
                sourceEntity.ActiveBranchId = null;
                sourceEntity.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        // Mark branch as retired
        var retired = branch with
        {
            Status = "retired",
            LifecycleStatus = MapStatusToLifecycleLabel("retired"),
            StageSummary = "私有分支已废弃",
            PrimarySignal = "已废弃",
            SignalLevel = "ok",
            PendingActions = []
        };
        await store.UpsertAsync(owner, retired, cancellationToken);
        await UpsertInstanceRecordAsync(retired, cancellationToken: cancellationToken);

        // Return source clone detail if available
        if (!string.IsNullOrWhiteSpace(sourceInstanceId))
        {
            var source = await store.FindAsync(sourceInstanceId, cancellationToken);
            if (source is not null)
            {
                return ApiResponse<EmployeeDetailDto>.SuccessResponse(source, wasLive
                    ? "私有分支已废弃，IM 路由已恢复到原分身"
                    : "私有分支已废弃");
            }
        }

        return ApiResponse<EmployeeDetailDto>.SuccessResponse(retired, "私有分支已废弃");
    }

    /// <summary>
    /// 恢复 IM 路由从私有分支到源分身。
    /// </summary>
    private async Task RestoreImRoutingToSourceAsync(
        string sourceInstanceId,
        string branchId,
        string owner,
        CancellationToken cancellationToken)
    {
        foreach (var platform in new[] { "feishu", "dingtalk", "wecom" })
        {
            try
            {
                // Clear branch's channel override
                var clearPath = platform switch
                {
                    "feishu" => "/admin/channels/feishu/override",
                    "dingtalk" => "/admin/channels/dingtalk/override",
                    "wecom" => "/admin/channels/wecom/override",
                    _ => null
                };
                if (clearPath is not null)
                {
                    await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
                        HttpMethod.Delete,
                        clearPath,
                        body: null,
                        owner,
                        cancellationToken,
                        useHireBotApiPrefix: false);
                }
            }
            catch
            {
                // Best-effort restoration
            }
        }
    }

    /// <summary>
    /// 当私有分支上岗时，切换 IM 路由到分支沙箱。
    /// </summary>
    private async Task SwitchImRoutingToBranchAsync(
        EmployeeDetailDto branch,
        string sourceInstanceId,
        string owner,
        CancellationToken cancellationToken)
    {
        // Set ActiveBranchId on source clone to enable in-app chat routing
        var sourceEntity = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == sourceInstanceId, cancellationToken);
        if (sourceEntity is not null)
        {
            sourceEntity.ActiveBranchId = branch.EmployeeId;
            sourceEntity.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
        await TryUpsertInstanceRecordsAsync(employees, cancellationToken);
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

    /// <summary>
    /// 确保种子数据存在。
    /// </summary>
    private async Task EnsureSeedDataAsync(string owner, CancellationToken cancellationToken)
    {
        var existing = await store.ListAsync(owner, cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var fixtureBundle = await LoadFixtureBundleAsync(owner, cancellationToken);
        if (fixtureBundle.Employees.Count > 0)
        {
            await store.ReplaceOwnerAsync(owner, fixtureBundle.Employees, cancellationToken);
            await teamImProvider.ReplaceItemsAsync(owner, fixtureBundle.TeamImItems, cancellationToken);
            await TryUpsertInstanceRecordsAsync(fixtureBundle.Employees, cancellationToken);
        }

        var persisted = await LoadPersistedRuntimeEmployeesAsync(owner, cancellationToken);
        if (persisted.Count == 0)
        {
            return;
        }

        if (fixtureBundle.Employees.Count == 0)
        {
            await store.ReplaceOwnerAsync(owner, persisted, cancellationToken);
            return;
        }

        await store.UpsertManyAsync(owner, persisted, cancellationToken);
    }

    /// <summary>
    /// 加载持久化的运行时员工数据。
    /// </summary>
    private async Task<IReadOnlyList<EmployeeDetailDto>> LoadPersistedRuntimeEmployeesAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        try
        {
            var instances = await dbContext.Instances
                .AsNoTracking()
                .Where(item => item.OwnerUserId == owner)
                .ToArrayAsync(cancellationToken);

            var employees = new List<EmployeeDetailDto>();
            foreach (var instance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var employee = !string.IsNullOrWhiteSpace(instance.RuntimeSnapshotJson)
                    ? DeserializeEmployeeSnapshot(instance.RuntimeSnapshotJson)
                    : await BuildEmployeeFromInstanceRecordAsync(instance, cancellationToken);
                if (employee is not null &&
                    string.Equals(employee.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
                {
                    employees.Add(employee);
                }
            }

            return employees;
        }
        catch
        {
            // Runtime snapshots are a persistence enhancement. Existing local databases may not have the column until migrations run.
            return [];
        }
    }

    /// <summary>
    /// 反序列化员工快照。
    /// </summary>
    private static EmployeeDetailDto? DeserializeEmployeeSnapshot(string snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<EmployeeDetailDto>(snapshot);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从实例记录构建员工详情。
    /// </summary>
    private async Task<EmployeeDetailDto?> BuildEmployeeFromInstanceRecordAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken)
    {
        EmployeeDetailDto? source = null;
        if (!string.IsNullOrWhiteSpace(instance.FromInstanceId))
        {
            source = await store.FindAsync(instance.FromInstanceId, cancellationToken);
        }

        var status = NormalizeStatus(instance.Status, null) ?? "hired";
        var type = string.IsNullOrWhiteSpace(instance.InstanceType) ? "department" : instance.InstanceType;
        var templateId = instance.BasedOnTemplateId ?? source?.BasedOnTemplateId ?? source?.SourceTemplateId ?? "unknown-template";
        var roleName = source?.RoleName ?? templateId;
        IReadOnlyList<EmployeeCapabilityDto> capabilities = source?.Capabilities.Count > 0
            ? source.Capabilities.Select(item => item with { Ready = status is "live" }).ToArray()
            : [new EmployeeCapabilityDto("站内对话", status is "live")];

        return new EmployeeDetailDto(
            EmployeeId: instance.InstanceId,
            Nickname: source is null ? instance.InstanceId : $"{source.Nickname} 的分身",
            RoleName: roleName,
            SourceTemplate: source?.SourceTemplate ?? templateId,
            SourceTemplateId: source?.SourceTemplateId ?? templateId,
            InstanceType: type,
            Status: status,
            BasedOnTemplateId: instance.BasedOnTemplateId,
            FromInstanceId: instance.FromInstanceId,
            OwnerUserId: instance.OwnerUserId,
            DepartmentId: instance.DepartmentId,
            LifecycleStatus: MapStatusToLifecycleLabel(status),
            StageSummary: type is "personal_clone" or "private_branch"
                ? "个人分身已恢复，站内对话可用"
                : BuildStageSummary(status, instance.InstanceId),
            PrimarySignal: BuildPrimarySignal(status),
            SignalLevel: status is "hired" or "interning_ai" ? "warn" : "ok",
            OwningTeam: instance.DepartmentId,
            CreatedAt: DateOnly.FromDateTime(instance.CreatedAt.UtcDateTime).ToString("yyyy-MM-dd"),
            InternshipStartAt: status is "live" ? DateOnly.FromDateTime(instance.CreatedAt.UtcDateTime).ToString("yyyy-MM-dd") : null,
            GraduatedAt: status is "live" ? DateOnly.FromDateTime(instance.UpdatedAt.UtcDateTime).ToString("yyyy-MM-dd") : null,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: [],
            Capabilities: capabilities,
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: capabilities.All(item => item.Ready));
    }

    /// <summary>
    /// 加载 Fixture 包。
    /// </summary>
    private static async Task<FixtureBundle> LoadFixtureBundleAsync(string owner, CancellationToken cancellationToken)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return new FixtureBundle([], [], 0);
        }

        var directories = Directory.GetDirectories(fixtureRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var employees = new List<EmployeeDetailDto>();
        var seedIndex = 0;
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var instancePath = Path.Combine(directory, "instance.json");
            if (!File.Exists(instancePath))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "manifest.json");

            try
            {
                var instanceContent = await File.ReadAllTextAsync(instancePath, cancellationToken);
                using var instanceDoc = JsonDocument.Parse(instanceContent);
                var root = instanceDoc.RootElement;

                var employeeId = TryGetString(root, "employeeId");
                if (string.IsNullOrWhiteSpace(employeeId))
                {
                    continue;
                }

                var templateId = TryGetString(root, "templateId", "fixture");
                var hireId = TryGetString(root, "hireId", employeeId);
                var scenario = TryGetString(root, "scenario", "fixture-collaboration");
                var generatedAtUtc = TryGetString(root, "generatedAtUtc");
                var explicitStatus = TryGetString(root, "status");

                var displayName = templateId;
                var capabilityNames = new List<string>();
                if (File.Exists(manifestPath))
                {
                    var manifestContent = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                    using var manifestDoc = JsonDocument.Parse(manifestContent);
                    var manifestRoot = manifestDoc.RootElement;
                    displayName = TryGetString(manifestRoot, "display_name", templateId);

                    if (manifestRoot.TryGetProperty("skills", out var skillsElement) &&
                        skillsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var skill in skillsElement.EnumerateArray())
                        {
                            var skillName = TryGetString(skill, "name");
                            if (!string.IsNullOrWhiteSpace(skillName))
                            {
                                capabilityNames.Add(skillName);
                            }
                        }
                    }
                }

                if (capabilityNames.Count == 0)
                {
                    capabilityNames.Add("scenario_parser");
                    capabilityNames.Add("report_generator");
                }

                var status = NormalizeStatus(explicitStatus, null) ?? ResolveFixtureSeedStatus(seedIndex++);
                var createdAt = ResolveCreatedAt(generatedAtUtc, seedIndex);
                var isReady = status is "live";
                var capabilities = capabilityNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new EmployeeCapabilityDto(name, isReady))
                    .ToArray();

                var employee = new EmployeeDetailDto(
                    EmployeeId: employeeId.Trim(),
                    Nickname: BuildFixtureNickname(displayName, employeeId),
                    RoleName: displayName,
                    SourceTemplate: displayName,
                    SourceTemplateId: templateId,
                    InstanceType: "department",
                    Status: status,
                    BasedOnTemplateId: templateId,
                    FromInstanceId: null,
                    OwnerUserId: owner,
                    DepartmentId: string.IsNullOrWhiteSpace(scenario) ? "department-default" : scenario,
                    LifecycleStatus: MapStatusToLifecycleLabel(status),
                    StageSummary: BuildStageSummary(status, hireId),
                    PrimarySignal: BuildPrimarySignal(status),
                    SignalLevel: status is "hired" or "interning_ai" ? "warn" : "ok",
                    OwningTeam: scenario,
                    CreatedAt: createdAt,
                    InternshipStartAt: status is "live" ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd") : null,
                    GraduatedAt: status is "live" ? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd") : null,
                    TasksDone: status is "live" ? 8 + seedIndex : 0,
                    TasksTotal: status is "live" ? 20 : 0,
                    SatisfactionScore: status is "live" ? 4.6m : null,
                    PendingActions: BuildPendingActions(status),
                    Capabilities: capabilities,
                    EvalPhase: status switch
                    {
                        "hired" => "pending_materials",
                        "interning_ai" => "pending_materials",
                        "interning_human" => "pending_review",
                        _ => null
                    },
                    EvalIteration: status is "interning_human" ? 1 : null,
                    EvalMaxIterations: status is "interning_human" ? 30 : null,
                    IsConfigured: capabilities.All(item => item.Ready));

                employees.Add(employee);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"解析 fixture 目录失败：{directory}", ex);
            }
        }

        return new FixtureBundle(
            Employees: employees,
            TeamImItems: BuildFixtureImItems(employees),
            FixtureDirectories: directories.Length);
    }

    /// <summary>
    /// 解析 Fixture 根目录。
    /// </summary>
    private static string? ResolveFixtureRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "InstanceFixtures"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "HireBot.ApiService", "Assets", "InstanceFixtures"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "InstanceFixtures")),
            Path.Combine(AppContext.BaseDirectory, "Assets", "InstanceFixtures")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// 加载 Fixture 模板绑定。
    /// </summary>
    private static IReadOnlyDictionary<string, FixtureTemplateBinding> LoadFixtureTemplateBindings()
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        var bindingPath = Path.Combine(fixtureRoot, "template-bindings.json");
        if (!File.Exists(bindingPath))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(bindingPath));
            var root = doc.RootElement;
            var items = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("bindings", out var bindings) &&
                     bindings.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(bindings.EnumerateArray());
            }

            var map = new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var templateId = TryGetString(item, "templateId");
                if (string.IsNullOrWhiteSpace(templateId))
                {
                    continue;
                }

                var fixtureTemplateId = TryGetString(item, "fixtureTemplateId");
                var fixtureEmployeeId = TryGetString(item, "fixtureEmployeeId");
                if (string.IsNullOrWhiteSpace(fixtureTemplateId) && string.IsNullOrWhiteSpace(fixtureEmployeeId))
                {
                    continue;
                }

                map[templateId.Trim()] = new FixtureTemplateBinding(
                    TemplateId: templateId.Trim(),
                    FixtureTemplateId: string.IsNullOrWhiteSpace(fixtureTemplateId) ? null : fixtureTemplateId.Trim(),
                    FixtureEmployeeId: string.IsNullOrWhiteSpace(fixtureEmployeeId) ? null : fixtureEmployeeId.Trim());
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 解析 Fixture 模板绑定。
    /// </summary>
    private static FixtureTemplateBinding? ResolveFixtureTemplateBinding(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return FixtureTemplateBindings.Value.TryGetValue(templateId.Trim(), out var binding)
            ? binding
            : null;
    }

    /// <summary>
    /// 判断 Fixture 模板是否匹配。
    /// </summary>
    private static bool IsFixtureTemplateMatch(
        EmployeeDetailDto employee,
        string requestedTemplateId,
        FixtureTemplateBinding? fixtureBinding)
    {
        if (fixtureBinding is not null)
        {
            if (!string.IsNullOrWhiteSpace(fixtureBinding.FixtureEmployeeId) &&
                string.Equals(employee.EmployeeId, fixtureBinding.FixtureEmployeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(fixtureBinding.FixtureTemplateId) &&
                (string.Equals(employee.BasedOnTemplateId, fixtureBinding.FixtureTemplateId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(employee.SourceTemplateId, fixtureBinding.FixtureTemplateId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return string.Equals(employee.BasedOnTemplateId, requestedTemplateId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(employee.SourceTemplateId, requestedTemplateId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 Fixture 种子状态。
    /// </summary>
    private static string ResolveFixtureSeedStatus(int index)
    {
        if (FixtureStatusSeedOrder.Length == 0)
        {
            return "hired";
        }

        return FixtureStatusSeedOrder[index % FixtureStatusSeedOrder.Length];
    }

    /// <summary>
    /// 构建 Fixture 昵称。
    /// </summary>
    private static string BuildFixtureNickname(string displayName, string employeeId)
    {
        var suffix = employeeId.Split('_').LastOrDefault() ?? "seed";
        return $"{displayName}-{suffix}";
    }

    /// <summary>
    /// 解析创建时间。
    /// </summary>
    private static string ResolveCreatedAt(string generatedAtUtc, int seedOffset)
    {
        if (DateTime.TryParse(generatedAtUtc, out var parsed))
        {
            return DateOnly.FromDateTime(parsed.ToLocalTime()).ToString("yyyy-MM-dd");
        }

        return DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-seedOffset)).ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 构建阶段摘要。
    /// </summary>
    private static string BuildStageSummary(string status, string hireId)
    {
        return status switch
        {
            "hired" => $"实例包 {hireId} 已生成，等待发起评估",
            "interning_ai" => "实例已准备完成，等待 AI 评估执行",
            "interning_human" => "AI 评估已通过，等待人工复核",
            "live" => "已上岗，稳定参与团队协作",
            "failed" => "评估未通过，等待回退处理",
            "retired" => "已退役，等待归档",
            _ => "状态更新中"
        };
    }

    /// <summary>
    /// 构建主要信号。
    /// </summary>
    private static string BuildPrimarySignal(string status)
    {
        return status switch
        {
            "hired" => "待操作：发起评估",
            "interning_ai" => "待执行 AI 评估",
            "interning_human" => "待人工审核",
            "live" => "运行稳定",
            "failed" => "评估未通过",
            "retired" => "实例已退役",
            _ => "状态同步中"
        };
    }

    /// <summary>
    /// 构建待办操作列表。
    /// </summary>
    private static string[] BuildPendingActions(string status)
    {
        return status switch
        {
            "hired" => ["确认团队归属", "检查技能配置"],
            "interning_ai" => ["准备评估材料", "确认评估场景"],
            "interning_human" => ["执行人工复核"],
            "live" => ["跟踪运行指标"],
            _ => []
        };
    }

    /// <summary>
    /// 构建 Fixture IM 项。
    /// </summary>
    private static IReadOnlyList<TeamImItemDto> BuildFixtureImItems(IReadOnlyList<EmployeeDetailDto> employees)
    {
        var now = DateTime.UtcNow;
        return employees
            .Select((employee, index) => new TeamImItemDto(
                ItemId: $"im_fixture_{employee.EmployeeId}",
                EmployeeId: employee.EmployeeId,
                EmployeeName: employee.Nickname,
                Category: ResolveImCategory(employee.Status),
                Content: BuildImContent(employee),
                Source: $"系统导入 · {employee.OwningTeam}",
                ReceivedAt: now.AddMinutes(-3 * (index + 1)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Status: "pending",
                ConfirmedAt: null))
            .ToArray();
    }

    /// <summary>
    /// 解析 IM 分类。
    /// </summary>
    private static string ResolveImCategory(string status)
    {
        return status switch
        {
            "hired" => "待办处理",
            "interning_ai" => "评估准备",
            "interning_human" => "人工复核",
            "live" => "运行协作",
            "failed" => "异常处理",
            "retired" => "归档状态",
            _ => "状态同步"
        };
    }

    /// <summary>
    /// 构建 IM 内容。
    /// </summary>
    private static string BuildImContent(EmployeeDetailDto employee)
    {
        return employee.Status switch
        {
            "hired" => $"实例 {employee.EmployeeId} 已导入，等待发起评估。",
            "interning_ai" => $"{employee.Nickname} 已具备评估材料，请执行 AI 评估。",
            "interning_human" => $"{employee.Nickname} AI 评估完成，请安排人工复核。",
            "live" => $"{employee.Nickname} 已上岗，关注协作反馈与稳定性。",
            "failed" => $"{employee.Nickname} 评估未通过，建议进入 Review 回退处理。",
            "retired" => $"{employee.Nickname} 已退役，相关路由将逐步清理。",
            _ => $"{employee.Nickname} 状态已更新，请确认团队协作安排。"
        };
    }

    /// <summary>
    /// 尝试获取 JSON 字符串值。
    /// </summary>
    private static string TryGetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    /// <summary>
    /// 转换为摘要。
    /// </summary>
    private static EmployeeSummaryDto ToSummary(EmployeeDetailDto detail)
    {
        var status = NormalizeStatus(detail.Status, detail.LifecycleStatus) ?? "hired";
        var lifecycleStatus = string.IsNullOrWhiteSpace(detail.LifecycleStatus)
            ? MapStatusToLifecycleLabel(status)
            : detail.LifecycleStatus;
        return new EmployeeSummaryDto(
            detail.EmployeeId,
            detail.Nickname,
            detail.RoleName,
            detail.SourceTemplate,
            detail.SourceTemplateId,
            string.IsNullOrWhiteSpace(detail.InstanceType) ? "department" : detail.InstanceType,
            status,
            detail.BasedOnTemplateId,
            detail.FromInstanceId,
            string.IsNullOrWhiteSpace(detail.OwnerUserId) ? "unknown" : detail.OwnerUserId,
            string.IsNullOrWhiteSpace(detail.DepartmentId) ? "department-default" : detail.DepartmentId,
            lifecycleStatus,
            detail.StageSummary,
            detail.PrimarySignal,
            detail.SignalLevel,
            detail.OwningTeam,
            detail.CreatedAt,
            detail.TasksDone,
            detail.TasksTotal,
            detail.PendingActions,
            detail.IsConfigured);
    }

    /// <summary>
    /// 批量插入或更新实例记录。
    /// </summary>
    private async Task UpsertInstanceRecordsAsync(
        IReadOnlyList<EmployeeDetailDto> employees,
        CancellationToken cancellationToken)
    {
        foreach (var employee in employees)
        {
            await UpsertInstanceRecordAsync(employee, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// 尝试批量插入或更新实例记录。
    /// </summary>
    private async Task TryUpsertInstanceRecordsAsync(
        IReadOnlyList<EmployeeDetailDto> employees,
        CancellationToken cancellationToken)
    {
        try
        {
            await UpsertInstanceRecordsAsync(employees, cancellationToken);
        }
        catch
        {
            // Local demo seed should still succeed even when the instance table has not been migrated yet.
        }
    }

    /// <summary>
    /// 插入或更新实例记录。
    /// </summary>
    private async Task UpsertInstanceRecordAsync(
        EmployeeDetailDto employee,
        bool viaQuickClone = false,
        string? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.Instances
            .FirstOrDefaultAsync(item => item.InstanceId == employee.EmployeeId, cancellationToken);

        var version = string.IsNullOrWhiteSpace(currentVersion)
            ? existing?.CurrentVersion ?? "v_initial"
            : currentVersion.Trim();

        if (existing is null)
        {
            dbContext.Instances.Add(new InstanceEntity
            {
                InstanceId = employee.EmployeeId,
                TenantId = ResolveTenantId(employee),
                InstanceType = string.IsNullOrWhiteSpace(employee.InstanceType) ? "department" : employee.InstanceType,
                Status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? "hired",
                ViaQuickClone = viaQuickClone,
                BasedOnTemplateId = employee.BasedOnTemplateId,
                FromInstanceId = employee.FromInstanceId,
                EvalReportId = null,
                OwnerUserId = string.IsNullOrWhiteSpace(employee.OwnerUserId) ? "unknown" : employee.OwnerUserId,
                DepartmentId = string.IsNullOrWhiteSpace(employee.DepartmentId) ? "department-default" : employee.DepartmentId,
                CurrentVersion = version,
                RuntimeSnapshotJson = JsonSerializer.Serialize(employee),
                CreatedAt = ParseDate(employee.CreatedAt) ?? now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.TenantId = ResolveTenantId(employee);
            existing.InstanceType = string.IsNullOrWhiteSpace(employee.InstanceType) ? existing.InstanceType : employee.InstanceType;
            existing.Status = NormalizeStatus(employee.Status, employee.LifecycleStatus) ?? existing.Status;
            existing.ViaQuickClone = viaQuickClone || existing.ViaQuickClone;
            existing.BasedOnTemplateId = employee.BasedOnTemplateId;
            existing.FromInstanceId = employee.FromInstanceId;
            existing.OwnerUserId = string.IsNullOrWhiteSpace(employee.OwnerUserId) ? existing.OwnerUserId : employee.OwnerUserId;
            existing.DepartmentId = string.IsNullOrWhiteSpace(employee.DepartmentId) ? existing.DepartmentId : employee.DepartmentId;
            existing.CurrentVersion = version;
            existing.RuntimeSnapshotJson = JsonSerializer.Serialize(employee);
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 解析日期。
    /// </summary>
    private static DateTimeOffset? ParseDate(string value)
    {
        if (DateOnly.TryParse(value, out var date))
        {
            return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// 解析租户ID。
    /// </summary>
    private static string ResolveTenantId(EmployeeDetailDto employee)
    {
        if (!string.IsNullOrWhiteSpace(employee.DepartmentId) &&
            !string.Equals(employee.DepartmentId, "department-default", StringComparison.OrdinalIgnoreCase))
        {
            return employee.DepartmentId;
        }

        return string.IsNullOrWhiteSpace(employee.OwningTeam) ? "tenant-default" : employee.OwningTeam;
    }

    /// <summary>
    /// 构建员工ID。
    /// </summary>
    private static string BuildEmployeeId()
    {
        return $"e_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..24];
    }

    /// <summary>
    /// 构建实例ID。
    /// </summary>
    private static string BuildInstanceId(string prefix)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "i" : prefix.Trim().Trim('_');
        return $"{normalizedPrefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..Math.Min(32, normalizedPrefix.Length + 1 + 13 + 1 + 32)];
    }

    /// <summary>
    /// 构建运行时作用域键。
    /// </summary>
    private static string BuildRuntimeScopeKey(string instanceId)
        => $"instance:{instanceId.Trim()}";

    /// <summary>
    /// 初始化个人分身沙箱。
    /// </summary>
    private async Task<ApiResponse<PersonalCloneSandboxSetupResult>> InitializePersonalCloneSandboxAsync(
        EmployeeDetailDto clone,
        InstanceArtifactCloneResult artifactResult,
        string ownerSubject,
        string tenantId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var scopeKey = BuildRuntimeScopeKey(clone.EmployeeId);
        var createResponse = await sandboxService.CreateAsync(
            new SandboxCreateRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = scopeKey,
                SandboxRole = RuntimeSandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                ProvisioningMode = "managed",
                UseCase = $"runtime-chat-for:{clone.EmployeeId}"
            },
            cancellationToken);
        if (!createResponse.Success || createResponse.Data is null)
        {
            return ApiResponse<PersonalCloneSandboxSetupResult>.ErrorResponse(createResponse.Code, createResponse.Message);
        }

        var readyResponse = await WaitForManagedSandboxReadyAsync(createResponse.Data, cancellationToken);
        if (!readyResponse.Success || readyResponse.Data is null)
        {
            await TryDeleteSandboxAsync(ownerSubject, scopeKey, createResponse.Data.SandboxId, cancellationToken);
            return ApiResponse<PersonalCloneSandboxSetupResult>.ErrorResponse(readyResponse.Code, readyResponse.Message);
        }

        var archiveBytes = BuildArtifactArchiveBytes(artifactResult.TargetRootPath);
        var uploadResponse = await kingCrabHttpClient.SendMultipartForJsonAsync<DigitalEmployeeUploadResponse>(
            "/admin/digital-employee/upload",
            "file",
            $"{clone.EmployeeId}-{artifactResult.CurrentVersion}.zip",
            archiveBytes,
            "application/zip",
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: readyResponse.Data.GatewayEndpoint);

        if (!uploadResponse.Success || uploadResponse.Data is null || !uploadResponse.Data.Success)
        {
            await TryDeleteSandboxAsync(ownerSubject, scopeKey, readyResponse.Data.SandboxId, cancellationToken);
            var message = uploadResponse.Success && uploadResponse.Data is not null && !string.IsNullOrWhiteSpace(uploadResponse.Data.Error)
                ? uploadResponse.Data.Error
                : uploadResponse.Message;
            return ApiResponse<PersonalCloneSandboxSetupResult>.ErrorResponse(
                uploadResponse.StatusCode <= 0 ? 502 : uploadResponse.StatusCode,
                string.IsNullOrWhiteSpace(message) ? "个人分身沙箱初始化失败" : message);
        }

        return ApiResponse<PersonalCloneSandboxSetupResult>.SuccessResponse(
            new PersonalCloneSandboxSetupResult(readyResponse.Data.SandboxId, readyResponse.Data.GatewayEndpoint));
    }

    /// <summary>
    /// 等待托管沙箱就绪。
    /// </summary>
    private async Task<ApiResponse<SandboxInstanceDto>> WaitForManagedSandboxReadyAsync(
        SandboxInstanceDto instance,
        CancellationToken cancellationToken)
    {
        if (string.Equals(instance.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(instance.GatewayEndpoint))
        {
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(instance);
        }

        for (var attempt = 0; attempt < 36; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var refreshResult = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = instance.SandboxId
                },
                cancellationToken);
            if (!refreshResult.Success || refreshResult.Data is null)
            {
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
            }

            if (string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
            {
                return ApiResponse<SandboxInstanceDto>.SuccessResponse(refreshResult.Data);
            }
        }

        return ApiResponse<SandboxInstanceDto>.ErrorResponse(504, "个人分身 sandbox 启动超时");
    }

    /// <summary>
    /// 尝试删除沙箱。
    /// </summary>
    private async Task TryDeleteSandboxAsync(
        string ownerSubject,
        string scopeKey,
        string sandboxId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sandboxService.DeleteAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = sandboxId,
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = scopeKey,
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject
                },
                cancellationToken);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// 退役时清理该实例的运行时资源。
    /// </summary>
    private async Task CleanupRetiredInstanceArtifactsAsync(
        string ownerSubject,
        string instanceId,
        CancellationToken cancellationToken)
    {
        await TryDeleteRuntimeSandboxAsync(ownerSubject, instanceId, cancellationToken);
        await RemoveInstanceImConfigsAsync(instanceId, cancellationToken);
    }

    /// <summary>
    /// 尝试删除分身运行时沙箱。
    /// </summary>
    private async Task TryDeleteRuntimeSandboxAsync(
        string ownerSubject,
        string instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await sandboxService.DeleteAsync(
                new SandboxInstanceLookupRequestDto
                {
                    ScopeType = SandboxScopeTypes.Hire,
                    ScopeKey = BuildRuntimeScopeKey(instanceId),
                    SandboxRole = RuntimeSandboxRole,
                    OwnerSubject = ownerSubject
                },
                cancellationToken);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// 删除该实例已配置的 IM 绑定。
    /// </summary>
    private async Task RemoveInstanceImConfigsAsync(string instanceId, CancellationToken cancellationToken)
    {
        foreach (var platform in new[] { "feishu", "dingtalk", "wecom" })
        {
            await TryDeleteChannelOverrideAsync(platform, cancellationToken);
        }
    }

    /// <summary>
    /// 删除沙箱内指定频道的运行时覆盖配置。
    /// </summary>
    private async Task TryDeleteChannelOverrideAsync(string platform, CancellationToken cancellationToken)
    {
        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        var path = normalizedPlatform switch
        {
            "feishu" => "/admin/channels/feishu/override",
            "dingtalk" => "/admin/channels/dingtalk/override",
            "wecom" => "/admin/channels/wecom/override",
            _ => null
        };

        if (path is null)
        {
            return;
        }

        try
        {
            var ownerSubject = requestContextService.ResolveOwnerSubject();
            await kingCrabHttpClient.SendForJsonAsync<KingCrabOperationStatusResult>(
                HttpMethod.Delete,
                path,
                body: null,
                ownerSubject,
                cancellationToken,
                useHireBotApiPrefix: false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// KingCrab 管理接口的通用操作结果。
    /// </summary>
    private sealed class KingCrabOperationStatusResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public string? Mode { get; set; }
    }

    /// <summary>
    /// 构建产物归档字节数组。
    /// </summary>
    private static byte[] BuildArtifactArchiveBytes(string artifactRoot)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var directory in Directory.EnumerateDirectories(artifactRoot, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = Path.GetRelativePath(artifactRoot, directory)
                    .Replace('\\', '/')
                    .Trim('/');
                if (string.IsNullOrWhiteSpace(relativeDirectory))
                {
                    continue;
                }

                var directoryEntry = archive.CreateEntry($"{relativeDirectory}/");
                directoryEntry.LastWriteTime = File.GetLastWriteTimeUtc(directory);
            }

            foreach (var sourcePath in Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(artifactRoot, sourcePath).Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(sourcePath);
                fileStream.CopyTo(entryStream);
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// 个人分身沙箱设置结果。
    /// </summary>
    private sealed record PersonalCloneSandboxSetupResult(string SandboxId, string? GatewayEndpoint);

    /// <summary>
    /// 数字员工上传响应。
    /// </summary>
    private sealed record DigitalEmployeeUploadResponse(
        bool Success,
        string? Error,
        string? Name,
        int SkillsInstalled,
        IReadOnlyList<string>? InstalledFiles,
        int? TotalSkillsLoaded);

    /// <summary>
    /// 获取第一个非空值。
    /// </summary>
    private static string Coalesce(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 判断状态流转是否允许。
    /// </summary>
    private static bool IsAllowedTransition(string from, string to)
    {
        if (!AllowedStatusTransitions.TryGetValue(from.Trim(), out var allowed))
        {
            return false;
        }

        return allowed.Contains(to.Trim());
    }

    /// <summary>
    /// 判断是否为可上传技能的实例。
    /// </summary>
    private static bool IsUploadSkillReadyInstance(EmployeeDetailDto employee)
    {
        var status = NormalizeStatus(employee.Status, employee.LifecycleStatus);
        if (!string.Equals(status, "interning_ai", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(employee.EvalPhase))
        {
            return true;
        }

        var phase = employee.EvalPhase.Trim().ToLowerInvariant();
        return phase is "pending_materials" or "pending_skill_upload" or "ai_running";
    }

    /// <summary>
    /// 规范化状态。
    /// </summary>
    private static string? NormalizeStatus(string? status, string? lifecycleStatus)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            return normalized switch
            {
                "hired" => "hired",
                "interning_ai" => "interning_ai",
                "interning_human" => "interning_human",
                "live" => "live",
                "failed" => "failed",
                "retired" => "retired",
                _ => null
            };
        }

        if (!string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            var normalized = lifecycleStatus.Trim().ToLowerInvariant();
            return normalized switch
            {
                "hired" or "待入职" or "已雇佣" => "hired",
                "interning_ai" or "ai评估" or "ai审核" => "interning_ai",
                "interning_human" or "人工评估" or "人工审核" => "interning_human",
                "live" or "上岗" or "在职" => "live",
                "failed" or "失败" => "failed",
                "retired" or "退役" or "离职" => "retired",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// 将状态映射到生命周期标签。
    /// </summary>
    private static string MapStatusToLifecycleLabel(string status)
    {
        return status switch
        {
            "hired" => "已雇佣",
            "interning_ai" => "AI评估中",
            "interning_human" => "人工复核",
            "live" => "已上岗",
            "failed" => "评估失败",
            "retired" => "已退役",
            _ => status
        };
    }
}
