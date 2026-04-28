namespace HireBot.Core.Services.Hiring.Storage;

public interface IHiringFileStore
{
    Task<string> SaveAsync(
        string sessionId,
        string category,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);
}

