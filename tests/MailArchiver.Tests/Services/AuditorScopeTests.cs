using System.Security.Cryptography;
using System.Text;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services;
using MailArchiver.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// A restricted auditor (assigned mailboxes and/or audit period) sees, through the global
/// query filters of the DbContext, only emails, attachments, originals and mail accounts
/// within that scope. Without a scope in the request nothing is filtered.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class AuditorScopeTests
{
    private readonly TestDbFixture _fixture;
    public AuditorScopeTests(TestDbFixture fixture) => _fixture = fixture;

    private static MailAccount Account() => new()
    {
        Name = $"scope-{Guid.NewGuid():N}".Substring(0, 25),
        EmailAddress = $"{Guid.NewGuid():N}@test.local",
        Provider = ProviderType.IMPORT,
        IsEnabled = false,
        LastSync = DateTime.UtcNow
    };

    private static ArchivedEmail Email(MailAccount account, DateTime sent) => new()
    {
        MailAccountId = account.Id,
        MessageId = $"<{Guid.NewGuid():N}@test>",
        Subject = "s", Body = "b", HtmlBody = string.Empty,
        From = "a@test.local", To = "b@test.local", Cc = string.Empty, Bcc = string.Empty,
        SentDate = sent, ReceivedDate = sent, FolderName = "INBOX"
    };

    /// <summary>A second context on the same connection and transaction, seeing the given scope.</summary>
    private static MailArchiverDbContext ScopedContext(MailArchiverDbContext seed, AuditorScope? scope)
    {
        var http = new DefaultHttpContext();
        if (scope != null) http.Items[AuditorScope.ItemKey] = scope;
        var options = new DbContextOptionsBuilder<MailArchiverDbContext>()
            .UseNpgsql(seed.Database.GetDbConnection())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        var ctx = new MailArchiverDbContext(options, new HttpContextAccessor { HttpContext = http });
        ctx.Database.UseTransaction(seed.Database.CurrentTransaction!.GetDbTransaction());
        return ctx;
    }

    [Fact]
    public async Task RestrictedAuditor_SeesOnlyAssignedMailboxesWithinThePeriod()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var assigned = Account();
        var other = Account();
        ctx.MailAccounts.AddRange(assigned, other);
        await ctx.SaveChangesAsync();

        var inside = Email(assigned, new DateTime(2024, 3, 1, 10, 0, 0));
        var lastDay = Email(assigned, new DateTime(2024, 6, 30, 23, 30, 0));
        var before = Email(assigned, new DateTime(2023, 12, 31, 23, 59, 0));
        var after = Email(assigned, new DateTime(2024, 7, 1, 0, 0, 0));
        var otherMailbox = Email(other, new DateTime(2024, 3, 1, 10, 0, 0));
        var all = new[] { inside, lastDay, before, after, otherMailbox };
        ctx.ArchivedEmails.AddRange(all);
        await ctx.SaveChangesAsync();

        foreach (var email in new[] { inside, before })
        {
            ctx.EmailAttachments.Add(new EmailAttachment
            {
                ArchivedEmailId = email.Id, FileName = "a.txt", ContentType = "text/plain",
                Size = 1, LegacyContent = new byte[] { 1 }
            });
            var raw = Encoding.ASCII.GetBytes($"Subject: {email.Id}\r\n\r\nx\r\n");
            ctx.ArchivedEmailSources.Add(new ArchivedEmailSource
            {
                ArchivedEmailId = email.Id, RawMime = raw,
                Sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()
            });
        }
        await ctx.SaveChangesAsync();

        var ids = all.Select(e => e.Id).ToList();
        var accountIds = new[] { assigned.Id, other.Id };

        await using (var restricted = ScopedContext(ctx, new AuditorScope(
                         new List<int> { assigned.Id }, new DateTime(2024, 1, 1), new DateTime(2024, 6, 30))))
        {
            Assert.Equal(new[] { inside.Id, lastDay.Id }.OrderBy(i => i),
                await restricted.ArchivedEmails.Where(e => ids.Contains(e.Id)).Select(e => e.Id).OrderBy(i => i).ToListAsync());
            Assert.Equal(new[] { inside.Id },
                await restricted.EmailAttachments.Where(a => ids.Contains(a.ArchivedEmailId)).Select(a => a.ArchivedEmailId).ToListAsync());
            Assert.Equal(new[] { inside.Id },
                await restricted.ArchivedEmailSources.Where(s => ids.Contains(s.ArchivedEmailId)).Select(s => s.ArchivedEmailId).ToListAsync());
            Assert.Equal(new[] { assigned.Id },
                await restricted.MailAccounts.Where(a => accountIds.Contains(a.Id)).Select(a => a.Id).ToListAsync());
            // Direct access by id finds nothing outside the scope
            Assert.Null(await restricted.ArchivedEmails.FirstOrDefaultAsync(e => e.Id == otherMailbox.Id));
        }

        // Period only: every mailbox, but only within the period
        await using (var periodOnly = ScopedContext(ctx, new AuditorScope(null, new DateTime(2024, 1, 1), null)))
        {
            Assert.Equal(4, await periodOnly.ArchivedEmails.CountAsync(e => ids.Contains(e.Id)));
            Assert.Equal(2, await periodOnly.MailAccounts.CountAsync(a => accountIds.Contains(a.Id)));
        }

        // No scope (admin, normal user, background job): nothing filtered
        await using (var unrestricted = ScopedContext(ctx, null))
        {
            Assert.Equal(5, await unrestricted.ArchivedEmails.CountAsync(e => ids.Contains(e.Id)));
            Assert.Equal(2, await unrestricted.EmailAttachments.CountAsync(a => ids.Contains(a.ArchivedEmailId)));
        }

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task LoadAsync_UsesAssignmentsAndPeriod_AndIsNullWhenNothingIsLimited()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var account = Account();
        ctx.MailAccounts.Add(account);
        var free = new User { Username = $"aud-{Guid.NewGuid():N}", Email = "f@test.local", IsAuditor = true };
        var limited = new User
        {
            Username = $"aud-{Guid.NewGuid():N}", Email = "l@test.local", IsAuditor = true,
            AuditFromDate = new DateTime(2024, 1, 1), AuditToDate = new DateTime(2024, 12, 31)
        };
        var periodOnly = new User { Username = $"aud-{Guid.NewGuid():N}", Email = "p@test.local", IsAuditor = true, AuditFromDate = new DateTime(2024, 1, 1) };
        ctx.Users.AddRange(free, limited, periodOnly);
        await ctx.SaveChangesAsync();
        ctx.UserMailAccounts.Add(new UserMailAccount { UserId = limited.Id, MailAccountId = account.Id });
        await ctx.SaveChangesAsync();

        Assert.Null(await AuditorScope.LoadAsync(ctx, free.Username));

        var loaded = await AuditorScope.LoadAsync(ctx, limited.Username);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { account.Id }, loaded!.AccountIds);
        Assert.Equal(new DateTime(2024, 1, 1), loaded.FromDate);
        Assert.Equal(new DateTime(2024, 12, 31), loaded.ToDate);

        var onlyPeriod = await AuditorScope.LoadAsync(ctx, periodOnly.Username);
        Assert.Null(onlyPeriod!.AccountIds);
        Assert.Null(onlyPeriod.ToDate);

        await scope.RollbackAsync();
    }
}
