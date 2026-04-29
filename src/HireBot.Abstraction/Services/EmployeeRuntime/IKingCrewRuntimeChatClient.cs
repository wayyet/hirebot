using HireBot.Abstraction.Models.EmployeeRuntime;

namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IKingCrewRuntimeChatClient
{
    Task<ApiResponse<RuntimeChatResponseDto>> SendAsync(
        RuntimeChatRequestDto request,
        CancellationToken cancellationToken = default);
}

