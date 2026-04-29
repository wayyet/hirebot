namespace HireBot.Abstraction.Services.Security;

public interface ISecretProtector
{
    string? Protect(string? value);

    string? Unprotect(string? value);
}

