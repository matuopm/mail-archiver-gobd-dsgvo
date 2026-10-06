namespace MailArchiver.Models
{
    /// <summary>
    /// A pause of the automatic deletion after the retention period, e.g. while a tax
    /// audit is running (§ 147 (3) AO: the period does not end while it is needed for an
    /// audit). The pause is active while a row with <see cref="EndedAt"/> = null exists.
    /// Rows are never deleted, so they also serve as the history of all pauses.
    /// </summary>
    public class RetentionHold
    {
        public int Id { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public string StartedBy { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime? EndedAt { get; set; }

        public string? EndedBy { get; set; }
    }
}
