using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Migration;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IEmployeeRuntimeService
{
    Task<ApiResponse<IReadOnlyList<EmployeeSummaryDto>>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> GetEmployeeAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<ApiResponse<ImportFixtureInstancesResultDto>> ImportFixtureInstancesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> UpdateLifecycleAsync(string employeeId, UpdateEmployeeLifecycleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> UpdateCapabilitiesAsync(string employeeId, UpdateEmployeeCapabilitiesRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> CompletePendingActionAsync(string employeeId, string actionId, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDetailDto>> CreateFromHireAsync(CreateEmployeeFromHireRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LocalStateMigrationResultDto>> MigrateLocalStateAsync(LocalStateMigrationRequestDto request, CancellationToken cancellationToken = default);
}
