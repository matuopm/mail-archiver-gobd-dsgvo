using MailArchiver.Data;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.Services.Shared
{
    /// <summary>
    /// Lookups for emails whose original message (archive_worm."ArchivedEmailSources") is
    /// still within its retention period. The database rejects deleting such an email, so
    /// every deletion path checks here first and skips or refuses instead of failing.
    /// </summary>
    public static class RetainedSources
    {
        /// <summary>Ids of emails whose stored original may not be deleted yet.</summary>
        public static IQueryable<int> RetainedEmailIds(MailArchiverDbContext context) =>
            context.ArchivedEmailSources
                .Where(s => s.RetainUntil > DateTime.UtcNow)
                .Select(s => s.ArchivedEmailId);

        /// <summary>Whether any email of the account still has a retained original.</summary>
        public static Task<bool> AccountHasRetainedAsync(MailArchiverDbContext context, int accountId,
            CancellationToken cancellationToken = default) =>
            context.ArchivedEmailSources
                .AnyAsync(s => s.RetainUntil > DateTime.UtcNow && s.ArchivedEmail.MailAccountId == accountId,
                    cancellationToken);

        /// <summary>
        /// How many emails of the account have a retained original, and until when the last
        /// of them is retained (the earliest time the account could be deleted).
        /// </summary>
        public static async Task<(int Count, DateTime? RetainedUntil)> AccountRetentionAsync(
            MailArchiverDbContext context, int accountId, CancellationToken cancellationToken = default)
        {
            var retained = context.ArchivedEmailSources
                .Where(s => s.RetainUntil > DateTime.UtcNow && s.ArchivedEmail.MailAccountId == accountId);
            var count = await retained.CountAsync(cancellationToken);
            if (count == 0)
                return (0, null);
            return (count, await retained.MaxAsync(s => s.RetainUntil, cancellationToken));
        }

        /// <summary>The subset of <paramref name="emailIds"/> whose originals are still retained.</summary>
        public static Task<List<int>> FilterRetainedAsync(MailArchiverDbContext context, ICollection<int> emailIds,
            CancellationToken cancellationToken = default) =>
            RetainedEmailIds(context)
                .Where(id => emailIds.Contains(id))
                .ToListAsync(cancellationToken);

        /// <summary>User-facing reason shown when a deletion is refused.</summary>
        public const string RetainedMessage =
            "The original of this email is stored write-once and still within its retention period, so it cannot be deleted yet.";
    }
}
