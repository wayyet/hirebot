using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Migration;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Collaboration;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class MockEmployeeRuntimeService(
    IEmployeeRuntimeStore store,
    ITemplateDataProvider templateDataProvider,
    ICollaborationService collaborationService,
    IRequestContextService requestContextService) : IEmployeeRuntimeService
{
    public async Task<ApiResponse<IReadOnlyList<EmployeeSummaryDto>>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var owner = requestContextService.ResolveOwnerSubject();
        await EnsureSeedDataAsync(owner, cancellationToken);

        var employees = await store.ListAsync(owner, cancellationToken);
        var summaries = employees.Select(ToSummary).ToArray();

        return ApiResponse<IReadOnlyList<EmployeeSummaryDto>>.SuccessResponse(summaries);
    }

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

    public async Task<ApiResponse<EmployeeDetailDto>> UpdateLifecycleAsync(
        string employeeId,
        UpdateEmployeeLifecycleRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || request is null || string.IsNullOrWhiteSpace(request.LifecycleStatus))
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(400, "employeeId 与 lifecycleStatus 为必填项");
        }

        var owner = requestContextService.ResolveOwnerSubject();
        var employee = await store.GetAsync(owner, employeeId.Trim(), cancellationToken);
        if (employee is null)
        {
            return ApiResponse<EmployeeDetailDto>.ErrorResponse(404, "员工不存在");
        }

        var normalizedStatus = request.LifecycleStatus.Trim();
        var updated = employee with
        {
            LifecycleStatus = normalizedStatus,
            StageSummary = Coalesce(request.StageSummary, employee.StageSummary),
            PrimarySignal = Coalesce(request.PrimarySignal, employee.PrimarySignal),
            SignalLevel = Coalesce(request.SignalLevel, employee.SignalLevel),
            InternshipStartAt = normalizedStatus == "实习中"
                ? Coalesce(request.InternshipStartAt, employee.InternshipStartAt, DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"))
                : employee.InternshipStartAt,
            GraduatedAt = normalizedStatus == "已转正"
                ? Coalesce(request.GraduatedAt, employee.GraduatedAt, DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"))
                : employee.GraduatedAt
        };

        await store.UpsertAsync(owner, updated, cancellationToken);
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(updated, "生命周期状态已更新");
    }

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
            LifecycleStatus: "待启动",
            StageSummary: "实例已生成，等待进入实习",
            PrimarySignal: "待操作：启动实习",
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
        return ApiResponse<EmployeeDetailDto>.SuccessResponse(employee, "员工实例已创建");
    }

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
        var archivedGroups = request.ArchivedGroupIds?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var archived = await collaborationService.MarkArchivedAsync(archivedGroups, cancellationToken);

        var result = new LocalStateMigrationResultDto(
            ImportedEmployees: imported,
            SkippedEmployees: Math.Max(0, (request.Employees?.Count ?? 0) - imported),
            ArchivedGroups: archived);

        return ApiResponse<LocalStateMigrationResultDto>.SuccessResponse(result, "本地状态迁移完成");
    }

    private async Task EnsureSeedDataAsync(string owner, CancellationToken cancellationToken)
    {
        var existing = await store.ListAsync(owner, cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var templates = await templateDataProvider.GetAllAsync(cancellationToken);
        var sample = templates.Take(4).ToArray();
        if (sample.Length == 0)
        {
            return;
        }

        var statuses = new[] { "待AI评估", "待人工评估", "实习中", "已转正" };
        var employees = new List<EmployeeDetailDto>();
        for (var i = 0; i < sample.Length; i++)
        {
            var template = sample[i];
            var status = statuses[Math.Min(i, statuses.Length - 1)];
            var capabilities = template.CoreAbilities.Select(item => new EmployeeCapabilityDto(item, status is "实习中" or "已转正")).ToArray();
            var isConfigured = capabilities.Length > 0 && capabilities.All(item => item.Ready);

            employees.Add(new EmployeeDetailDto(
                EmployeeId: BuildEmployeeId(),
                Nickname: template.Name,
                RoleName: template.Name,
                SourceTemplate: template.Name,
                SourceTemplateId: template.TemplateId,
                LifecycleStatus: status,
                StageSummary: status switch
                {
                    "待AI评估" => "等待 AI 评估",
                    "待人工评估" => "等待人工评估",
                    "实习中" => "实习中，积累评估数据",
                    _ => "已转正，正式运行"
                },
                PrimarySignal: status switch
                {
                    "待AI评估" => "待执行 AI 评估",
                    "待人工评估" => "待人工审核",
                    "实习中" => "运行正常",
                    _ => "运行稳定"
                },
                SignalLevel: status is "待AI评估" or "待人工评估" ? "warn" : "ok",
                OwningTeam: "默认团队",
                CreatedAt: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3 - i)).ToString("yyyy-MM-dd"),
                InternshipStartAt: status is "实习中" or "已转正" ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)).ToString("yyyy-MM-dd") : null,
                GraduatedAt: status is "已转正" ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd") : null,
                TasksDone: status is "实习中" or "已转正" ? 12 + i : 0,
                TasksTotal: status is "实习中" or "已转正" ? 20 : 0,
                SatisfactionScore: status is "已转正" ? 4.6m : null,
                PendingActions: status switch
                {
                    "待AI评估" => ["准备评估材料"],
                    "待人工评估" => ["确认上岗判定"],
                    _ => []
                },
                Capabilities: capabilities,
                EvalPhase: status is "待人工评估" ? "pending_review" : null,
                EvalIteration: status is "待人工评估" ? 2 : null,
                EvalMaxIterations: status is "待人工评估" ? 30 : null,
                IsConfigured: isConfigured));
        }

        await store.UpsertManyAsync(owner, employees, cancellationToken);
    }

    private static EmployeeSummaryDto ToSummary(EmployeeDetailDto detail)
    {
        return new EmployeeSummaryDto(
            detail.EmployeeId,
            detail.Nickname,
            detail.RoleName,
            detail.SourceTemplate,
            detail.SourceTemplateId,
            detail.LifecycleStatus,
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

    private static string BuildEmployeeId()
    {
        return $"e_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..24];
    }

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
}
