using System.ComponentModel.DataAnnotations;

namespace MailArchiver.Models
{
    public class User
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(320)]
        public string Username { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Email { get; set; }
        
        public string? PasswordHash { get; set; }
        
        [Required]
        public bool IsAdmin { get; set; } = false;
        
        [Required]
        public bool IsSelfManager { get; set; } = false;

        // Auditor (e.g. tax auditor): may read all mail accounts, changes nothing
        [Required]
        public bool IsAuditor { get; set; } = false;

        // Optional audit period of an auditor: only emails sent within it are visible
        // (dates inclusive). The mailboxes of an auditor are limited by assigning them;
        // an auditor without assignments sees every mailbox.
        public DateTime? AuditFromDate { get; set; }
        public DateTime? AuditToDate { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastLoginAt { get; set; }
        
        // 2FA TOTP properties
        public bool IsTwoFactorEnabled { get; set; } = false;
        public string? TwoFactorSecret { get; set; }
        public string? TwoFactorBackupCodes { get; set; }

        // OAuth properties
        public string? OAuthRemoteUserId { get; set; }
        public bool RequiresApproval { get; set; } = false;

        // Version update tracking
        public string? LastSeenChangelogVersion { get; set; }
        
        // Navigation properties
        public virtual ICollection<UserMailAccount> UserMailAccounts { get; set; } = new List<UserMailAccount>();
        public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
    }
}
