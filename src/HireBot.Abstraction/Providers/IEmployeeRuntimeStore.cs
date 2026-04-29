using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Providers;

public interface IEmployeeRuntimeStore
{
    Task<IReadOnlyList<EmployeeDetailDto>> ListAsync(string ownerSubject, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto?> GetAsync(string ownerSubject, string employeeId, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto?> FindAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<bool> ExistsNameAsync(string ownerSubject, string displayName, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto> UpsertAsync(string ownerSubject, EmployeeDetailDto employee, CancellationToken cancellationToken = default);
    Task<int> UpsertManyAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default);
    Task<int> ReplaceOwnerAsync(string ownerSubject, IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken = default);
}
