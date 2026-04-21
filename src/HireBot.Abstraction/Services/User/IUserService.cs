using HireBot.Abstraction.Models.User;

namespace HireBot.Abstraction.Services.User;

public interface IUserService
{
    Task<ApiResponse<UserDto>> GetUserByIdAsync(int id);
    Task<ApiResponse<IEnumerable<UserDto>>> GetAllUsersAsync();
    Task<ApiResponse<UserDto>> CreateUserAsync(CreateUserDto createUserDto);
    Task<ApiResponse<UserDto>> UpdateUserAsync(int id, UpdateUserDto updateUserDto);
    Task<ApiResponse<bool>> DeleteUserAsync(int id);
}