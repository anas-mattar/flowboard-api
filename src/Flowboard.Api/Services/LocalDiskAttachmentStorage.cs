// research.md R-1 / plan.md ADR-40: local-disk-backed IAttachmentStorage — the interim
// implementation for a project with no cloud object-store account/config anywhere yet
// (confirmed through 008). Files are keyed by a generated opaque name (Guid, "N" format),
// never the caller-supplied filename, so there is nothing here for a path-traversal or
// filename-collision attack to exploit.
namespace Flowboard.Api.Services;

public sealed class LocalDiskAttachmentStorage : IAttachmentStorage
{
    private const int BufferSize = 81920;

    private readonly string _rootPath;

    public LocalDiskAttachmentStorage(IConfiguration configuration, IHostEnvironment environment)
    {
        _rootPath = configuration["Attachments:StorageRootPath"]
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "attachments");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        var storageKey = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_rootPath, storageKey);

        await using var fileStream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storageKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, storageKey);
        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
