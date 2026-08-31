// research.md R-1 / plan.md ADR-40: the seam a future cloud-backed implementation (e.g.
// Azure Blob Storage) would fill without touching the Attachment entity, the API contract,
// or any calling code. storageKey is an opaque value this interface's own implementation
// generates (Attachment.StorageKey) — callers never construct or interpret it.
namespace Flowboard.Api.Services;

public interface IAttachmentStorage
{
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
