namespace MailArchiver.Models
{
    /// <summary>
    /// Configuration options for compliance storage (see doc/WormStorage.md).
    /// </summary>
    public class ComplianceOptions
    {
        public const string Compliance = "Compliance";

        /// <summary>
        /// When true, the IMAP sync stores every newly archived email's original MIME
        /// bytes and their SHA-256 in the write-once archive_worm schema, and fills
        /// ArchivedEmails.ContentHash. Stored originals cannot be deleted before their
        /// retention date, which also blocks retention, account and manual deletion of
        /// those emails. Roughly doubles the storage per email.
        /// Default: false
        /// </summary>
        public bool StoreOriginalMime { get; set; } = false;
    }
}
