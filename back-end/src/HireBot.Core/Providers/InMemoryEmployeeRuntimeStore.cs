using System.Collections.Concurrent;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class InMemoryEmployeeRuntimeStore : IEmployeeRuntimeStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, EmployeeDetailDto>> byOwner =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<EmployeeDetailDto>> ListAsync(string ownerSubject, CancellationToken cancellationToken = default)
    {
        if (!byOwner.TryGetValue(ownerSubject, out var employees))
        {
            return Task.FromResult<IReadOnlyList<EmployeeDetailDto>>([]);
        }

        return Task.FromResult<IReadOnlyList<EmployeeDetailDto>>(employees.Values
            .OrderByDescending(item => item.CreatedAt)
            .ToArray());
    }

    public Task<EmployeeDetailDto?> GetAsync(string ownerSubject, string employeeId, CancellationToken cancellationToken = default)
    {
        if (!byOwner.TryGetValue(ownerSubject, out var employees))
        {
            return Task.FromResult<EmployeeDetailDto?>(null);
        }

        return Task.FromResult(employees.TryGetValue(employeeId, out var employee)
            ? employee
            : null);
    }

    public Task<EmployeeDetailDto?> FindAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        foreach (var employees in byOwner.Values)
        {
            if (employees.TryGetValue(employeeId, out var employee))
            {
                return Task.FromResult<EmployeeDetailDto?>(employee);
            }
        }

        return Task.FromResult<EmployeeDetailDto?>(null);
    }

    public Task<bool> ExistsNameAsync(string ownerSubject, string displayName, CancellationToken cancellationToken = default)
    {
        if (!byOwner.TryGetValue(ownerSubject, out var employees))
        {
            return Task.FromResult(false);
        }

        var exists = employees.Values.Any(item =>
            (string.Equals(item.InstanceType, "personal_clone", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.InstanceType, "private_branch", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(item.Nickname, displayName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    public Task<EmployeeDetailDto> UpsertAsync(string ownerSubject, EmployeeDetailDto employee, CancellationToken cancellationToken = default)
    {
        var employees = byOwner.GetOrAdd(ownerSubject, _ => new ConcurrentDictionary<string, EmployeeDetailDto>(StringComparer.OrdinalIgnoreCase));
        employees[employee.EmployeeId] = employee;
        return Task.FromResult(employee);
    }

    public Task<int> UpsertManyAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default)
    {
        if (employees.Count == 0)
        {
            return Task.FromResult(0);
        }

        var bucket = byOwner.GetOrAdd(ownerSubject, _ => new ConcurrentDictionary<string, EmployeeDetailDto>(StringComparer.OrdinalIgnoreCase));
        var imported = 0;
        foreach (var employee in employees)
        {
            bucket[employee.EmployeeId] = employee;
            imported++;
        }

        return Task.FromResult(imported);
    }

    public Task<int> ReplaceOwnerAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default)
    {
        var bucket = new ConcurrentDictionary<string, EmployeeDetailDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in employees)
        {
            bucket[employee.EmployeeId] = employee;
        }

        byOwner[ownerSubject] = bucket;
        return Task.FromResult(bucket.Count);
    }
}
