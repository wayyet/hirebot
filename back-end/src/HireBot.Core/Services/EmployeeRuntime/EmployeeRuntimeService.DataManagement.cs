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

public sealed partial class EmployeeRuntimeService
{
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
                .OrderByDescending(item => item.UpdatedAt)
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
        return HireBotPathResolver.ResolveConventionalInstanceFixturesRoot();
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

}
