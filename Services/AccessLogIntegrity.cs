using MailArchiver.Data;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.Services
{
    /// <summary>
    /// Result of archive_worm.verify_access_log(): how many entries form an intact chain,
    /// the first entry that does not fit (null if none) and the hash of the last intact entry.
    /// Writing down <see cref="HeadHash"/> from time to time lets a later check also prove
    /// that no entries were cut off at the end.
    /// </summary>
    public sealed record AccessLogIntegrity(long Checked, long? FirstBrokenSeq, string? HeadHash)
    {
        public bool IsIntact => FirstBrokenSeq == null;

        public static async Task<AccessLogIntegrity> VerifyAsync(MailArchiverDbContext context,
            CancellationToken cancellationToken = default) =>
            await context.Database.SqlQueryRaw<AccessLogIntegrity>(
                    "SELECT checked AS \"Checked\", first_broken_seq AS \"FirstBrokenSeq\", head_hash AS \"HeadHash\" " +
                    "FROM archive_worm.verify_access_log()")
                .SingleAsync(cancellationToken);
    }
}
