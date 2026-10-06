using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MailArchiver.Services
{
    /// <summary>
    /// Deletes archived emails once the retention period of their stored original
    /// (archive_worm."ArchivedEmailSources"."RetainUntil") has passed. Runs once a day,
    /// and not at all while an administrator has paused it (see <see cref="RetentionHold"/>).
    /// Emails without a stored original are never deleted here.
    /// </summary>
    public class RetentionDeletionService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RetentionDeletionService> _logger;
        private readonly BatchOperationOptions _batchOptions;

        public RetentionDeletionService(IServiceScopeFactory scopeFactory, ILogger<RetentionDeletionService> logger,
            IOptions<BatchOperationOptions> batchOptions)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _batchOptions = batchOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<MailArchiverDbContext>();
                    await RunAsync(context, _logger, _batchOptions.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Automatic deletion after the retention period failed");
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// One deletion run. Returns the number of deleted emails, 0 while a pause is active.
        /// </summary>
        public static async Task<int> RunAsync(MailArchiverDbContext context, ILogger logger, int batchSize,
            CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
                batchSize = 500;

            var activeHold = await context.RetentionHolds
                .Where(h => h.EndedAt == null)
                .OrderBy(h => h.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (activeHold != null)
            {
                logger.LogInformation("Automatic deletion is paused since {StartedAt} by {StartedBy}: {Reason}",
                    activeHold.StartedAt, activeHold.StartedBy, activeHold.Reason);
                return 0;
            }

            var deleted = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var ids = await context.ArchivedEmailSources
                    .Where(s => s.RetainUntil <= now)
                    .OrderBy(s => s.ArchivedEmailId)
                    .Select(s => s.ArchivedEmailId)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);
                if (ids.Count == 0)
                    break;

                // The lock trigger only lets unlocked emails be deleted. Deleting the email
                // cascades to its original, which the archive_worm trigger allows because
                // RetainUntil has passed.
                await context.ArchivedEmails
                    .Where(e => ids.Contains(e.Id))
                    .ExecuteUpdateAsync(u => u.SetProperty(e => e.IsLocked, false), cancellationToken);
                await context.EmailAttachments
                    .Where(a => ids.Contains(a.ArchivedEmailId))
                    .ExecuteDeleteAsync(cancellationToken);
                var batchDeleted = await context.ArchivedEmails
                    .Where(e => ids.Contains(e.Id))
                    .ExecuteDeleteAsync(cancellationToken);
                deleted += batchDeleted;
                if (batchDeleted == 0)
                    break; // nothing removable left in this batch; avoid looping on it
            }

            if (deleted > 0)
            {
                logger.LogInformation("Automatic deletion after the retention period: deleted {Count} emails", deleted);
                context.AccessLogs.Add(new AccessLog
                {
                    Username = "System",
                    Type = AccessLogType.Retention,
                    Timestamp = DateTime.UtcNow,
                    SearchParameters = LogText.Event("RetentionAutoDeleted", deleted)
                });
                await context.SaveChangesAsync(cancellationToken);
            }

            return deleted;
        }
    }
}
