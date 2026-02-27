using Travora.Domain.Enums;

namespace Travora.Domain.Entities;

public class RefreshToken
{
    public int TokenId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int UserId { get; set; }
    public UserType UserType { get; set; }
}
