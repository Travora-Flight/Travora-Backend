using Travora.Domain.Common;
using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class Document : IHasTimestamps, ISoftDelete
{
    public int DocumentId { get; set; }
    public int OwnerId { get; set; }
    public DocumentOwnerType OwnerType { get; set; }
    public DocumentType DocumentType { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int FileSizeKb { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Pending;
    public DateTime? VerifiedAt { get; set; }
    public int VersionNumber { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Foreign keys
    public int? VerifiedByAdminId { get; set; }
    public int? ReplacedByDocumentId { get; set; }

    // Navigation properties
    public Admin? VerifiedByAdmin { get; set; }
    public Document? ReplacedByDocument { get; set; }
    public ICollection<PassportValidation> PassportValidations { get; set; } = new List<PassportValidation>();
}
