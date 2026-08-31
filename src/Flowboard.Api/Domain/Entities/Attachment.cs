// specs/009-card-attachments/data-model.md#Attachment. Metadata only — file bytes live in
// IAttachmentStorage (research.md R-1), never in this row or anywhere in SQL Server.
namespace Flowboard.Api.Domain.Entities;

public sealed class Attachment
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public int UploadedById { get; set; }

    public User UploadedBy { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
