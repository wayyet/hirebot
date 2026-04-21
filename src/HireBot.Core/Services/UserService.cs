using HireBot.Abstraction;
using HireBot.Abstraction.Models;

namespace HireBot.Core.Services;

public sealed class UserService(IHireBotRepository repository)
{
    public async Task<ApiResponse<UserDto>> GetUserByIdAsync(int id)
    {
        var user = await repository.GetByIdAsync<User>(id);
        if (user == null)
        {
            return ApiResponse<UserDto>.ErrorResponse(404, "用户不存在");
        }

        var userDto = new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email
        };

        return ApiResponse<UserDto>.SuccessResponse(userDto);
    }

    public async Task<ApiResponse<IEnumerable<UserDto>>> GetAllUsersAsync()
    {
        var users = await repository.GetAllAsync<User>();
        var userDtos = users.Select(user => new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email
        });

        return ApiResponse<IEnumerable<UserDto>>.SuccessResponse(userDtos);
    }

    public async Task<ApiResponse<UserDto>> CreateUserAsync(CreateUserDto createUserDto)
    {
        // 简单的密码哈希处理（实际项目中应该使用更安全的方式）
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(createUserDto.Password);

        var user = new User
        {
            Name = createUserDto.Name,
            Email = createUserDto.Email,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };

        var createdUser = await repository.AddAsync(user);

        var userDto = new UserDto
        {
            Id = createdUser.Id,
            Name = createdUser.Name,
            Email = createdUser.Email
        };

        return ApiResponse<UserDto>.SuccessResponse(userDto, "用户创建成功");
    }

    public async Task<ApiResponse<UserDto>> UpdateUserAsync(int id, UpdateUserDto updateUserDto)
    {
        var user = await repository.GetByIdAsync<User>(id);
        if (user == null)
        {
            return ApiResponse<UserDto>.ErrorResponse(404, "用户不存在");
        }

        user.Name = updateUserDto.Name;
        user.Email = updateUserDto.Email;
        user.UpdatedAt = DateTime.UtcNow;

        var updatedUser = await repository.UpdateAsync(user);

        var userDto = new UserDto
        {
            Id = updatedUser.Id,
            Name = updatedUser.Name,
            Email = updatedUser.Email
        };

        return ApiResponse<UserDto>.SuccessResponse(userDto, "用户更新成功");
    }

    public async Task<ApiResponse<bool>> DeleteUserAsync(int id)
    {
        var user = await repository.GetByIdAsync<User>(id);
        if (user == null)
        {
            return ApiResponse<bool>.ErrorResponse(404, "用户不存在");
        }

        await repository.DeleteAsync<User>(id);
        return ApiResponse<bool>.SuccessResponse(true, "用户删除成功");
    }
}
