namespace HireBot.Abstraction.Models;

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public class CreateUserDto
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
}

public class UpdateUserDto
{
    public string Name { get; set; }
    public string Email { get; set; }
}