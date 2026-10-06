namespace MailArchiver.Models
{
    /// <summary>
    /// The original MIME message of an archived email, exactly as it was received
    /// (byte for byte), together with its SHA-256 fingerprint.
    /// <para>
    /// Rows live in the separate <c>archive_worm</c> schema and are write-once:
    /// database triggers reject every UPDATE and TRUNCATE, recompute and verify the
    /// hash on INSERT, and reject a DELETE before <see cref="RetainUntil"/>. See
    /// <c>doc/WormStorage.md</c>.
    /// </para>
    /// </summary>
    public class ArchivedEmailSource
    {
        /// <summary>Id of the <see cref="ArchivedEmail"/> this source belongs to (1:1).</summary>
        public int ArchivedEmailId { get; set; }

        /// <summary>The raw RFC 5322 message bytes as delivered by the source.</summary>
        public byte[] RawMime { get; set; } = Array.Empty<byte>();

        /// <summary>Length of <see cref="RawMime"/> in bytes (set by the database on insert).</summary>
        public long Size { get; set; }

        /// <summary>SHA-256 of <see cref="RawMime"/> as lowercase hex (verified by the database on insert).</summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>When the source was captured (set by the database on insert, cannot be backdated).</summary>
        public DateTime CapturedAt { get; set; }

        /// <summary>
        /// No deletion before this time (set by the database on insert to the end of the
        /// 8th year after the capture year). Independent of <see cref="ArchivedEmail.IsLocked"/>.
        /// </summary>
        public DateTime RetainUntil { get; set; }

        /// <summary>Where the bytes came from, one of <see cref="ArchivedEmailSourceKinds"/>.</summary>
        public string Source { get; set; } = ArchivedEmailSourceKinds.Imap;

        public virtual ArchivedEmail ArchivedEmail { get; set; } = null!;
    }

    /// <summary>Allowed values for <see cref="ArchivedEmailSource.Source"/>.</summary>
    public static class ArchivedEmailSourceKinds
    {
        public const string Imap = "imap";
        public const string EmlImport = "eml-import";
        public const string MboxImport = "mbox-import";
        public const string Graph = "graph";

        /// <summary>
        /// Rebuilt from the stored fields because the original was no longer available.
        /// Protected from the capture time on, but not proof of the original wording.
        /// </summary>
        public const string Reconstructed = "reconstructed";
    }
}
