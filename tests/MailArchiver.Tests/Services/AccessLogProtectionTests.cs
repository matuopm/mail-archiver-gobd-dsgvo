using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Write-once access log with SHA-256 hash chain (migration MigrateV2610_3): new entries are
/// chained, UPDATE/DELETE/TRUNCATE are rejected, archive_worm.verify_access_log() detects a
/// changed entry, and the admin script carries the same SQL as the migration.
/// Every database test runs in a transaction that is rolled back.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class AccessLogProtectionTests
{
    private const string InsufficientPrivilege = "42501";
    private const string RaiseException = "P0001";

    private readonly TestDbFixture _fixture;
    public AccessLogProtectionTests(TestDbFixture fixture) => _fixture = fixture;

    private sealed record ChainRow(long ChainSeq, string? PrevHash, string Hash);
    private sealed record VerifyResult(long Checked, long? FirstBrokenSeq, string? HeadHash);

    private static async Task<AccessLog> AddLogAsync(MailArchiverDbContext ctx, string text)
    {
        var log = new AccessLog
        {
            Username = "chain-test",
            Type = AccessLogType.Search,
            Timestamp = DateTime.UtcNow,
            SearchParameters = text
        };
        ctx.AccessLogs.Add(log);
        await ctx.SaveChangesAsync();
        return log;
    }

    private static async Task<ChainRow> ChainOfAsync(MailArchiverDbContext ctx, int id) =>
        await ctx.Database.SqlQueryRaw<ChainRow>(
                "SELECT \"ChainSeq\", trim(\"PrevHash\") AS \"PrevHash\", trim(\"Hash\") AS \"Hash\" " +
                "FROM archive_worm.\"AccessLogs\" WHERE \"Id\" = {0}", id)
            .SingleAsync();

    private static async Task<VerifyResult> VerifyAsync(MailArchiverDbContext ctx) =>
        await ctx.Database.SqlQueryRaw<VerifyResult>(
                "SELECT checked AS \"Checked\", first_broken_seq AS \"FirstBrokenSeq\", head_hash AS \"HeadHash\" " +
                "FROM archive_worm.verify_access_log()")
            .SingleAsync();

    /// <summary>
    /// Runs <paramref name="action"/> inside a savepoint and returns the PostgreSQL error code
    /// it failed with, so the surrounding transaction stays usable after the expected error.
    /// </summary>
    private static async Task<string?> SqlStateOfAsync(MailArchiverDbContext ctx, Func<Task> action)
    {
        await ctx.Database.ExecuteSqlRawAsync("SAVEPOINT log_test");
        try
        {
            await action();
            await ctx.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT log_test");
            return null;
        }
        catch (Exception ex)
        {
            await ctx.Database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT log_test");
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is PostgresException pg) return pg.SqlState;
            }
            throw;
        }
    }

    [Fact]
    public async Task NewEntries_AreChainedToThePreviousOne()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;

        var first = await ChainOfAsync(ctx, (await AddLogAsync(ctx, "first")).Id);
        var second = await ChainOfAsync(ctx, (await AddLogAsync(ctx, "second")).Id);

        Assert.Equal(first.ChainSeq + 1, second.ChainSeq);
        Assert.Equal(first.Hash, second.PrevHash);
        Assert.Equal(64, second.Hash.Length);

        var result = await VerifyAsync(ctx);
        Assert.Null(result.FirstBrokenSeq);
        Assert.Equal(second.Hash, result.HeadHash);
        Assert.True(result.Checked >= second.ChainSeq);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Update_Delete_And_Truncate_AreRejected()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var log = await AddLogAsync(ctx, "keep me");

        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE archive_worm.\"AccessLogs\" SET \"SearchParameters\" = 'changed' WHERE \"Id\" = {0}", log.Id)));
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM archive_worm.\"AccessLogs\" WHERE \"Id\" = {0}", log.Id)));
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE archive_worm.\"AccessLogs\"")));

        // Also through EF
        ctx.ChangeTracker.Clear();
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, async () =>
        {
            ctx.AccessLogs.Remove(await ctx.AccessLogs.SingleAsync(l => l.Id == log.Id));
            await ctx.SaveChangesAsync();
        }));
        ctx.ChangeTracker.Clear();

        Assert.Equal("keep me", (await ctx.AccessLogs.SingleAsync(l => l.Id == log.Id)).SearchParameters);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Verify_FindsAnEntryChangedBehindTheTriggersBack()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var log = await AddLogAsync(ctx, "original text");
        await AddLogAsync(ctx, "later entry");
        var seq = (await ChainOfAsync(ctx, log.Id)).ChainSeq;

        // What only a database superuser could do: switch the protection off and edit
        await ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"AccessLogs\" DISABLE TRIGGER prevent_access_log_change");
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE archive_worm.\"AccessLogs\" SET \"SearchParameters\" = 'forged text' WHERE \"Id\" = {0}", log.Id);

        var result = await VerifyAsync(ctx);
        Assert.Equal(seq, result.FirstBrokenSeq);
        Assert.Equal(seq - 1, result.Checked);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Verify_FindsADeletedEntry()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var log = await AddLogAsync(ctx, "to be removed");
        await AddLogAsync(ctx, "after it");
        var seq = (await ChainOfAsync(ctx, log.Id)).ChainSeq;

        await ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"AccessLogs\" DISABLE TRIGGER prevent_access_log_change");
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM archive_worm.\"AccessLogs\" WHERE \"Id\" = {0}", log.Id);

        // The gap shows at the entry that followed the deleted one
        Assert.Equal(seq + 1, (await VerifyAsync(ctx)).FirstBrokenSeq);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Harden_LeavesTheAppRoleOnlyReadingAndAddingLogEntries()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var appRole = $"log_app_{suffix}";
        var ownerRole = $"log_owner_{suffix}";
        var existing = await AddLogAsync(ctx, "before hardening");

        await ctx.Database.ExecuteSqlRawAsync($"CREATE ROLE {appRole} NOLOGIN");
        await ctx.Database.ExecuteSqlRawAsync("SELECT archive_worm.harden({0}, {1})", appRole, ownerRole);
        await ctx.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {appRole}");

        var added = await AddLogAsync(ctx, "as application role");
        Assert.Equal((await ChainOfAsync(ctx, existing.Id)).Hash, (await ChainOfAsync(ctx, added.Id)).PrevHash);

        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "ALTER TABLE archive_worm.\"AccessLogs\" DISABLE TRIGGER prevent_access_log_change")));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM archive_worm.\"AccessLogs\" WHERE \"Id\" = {0}", added.Id)));
        Assert.Equal(InsufficientPrivilege, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DROP TABLE archive_worm.\"AccessLogs\"")));
        Assert.Null((await VerifyAsync(ctx)).FirstBrokenSeq);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task RunningTheSqlAgain_ChainsEntriesWrittenBeforeTheProtection()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        var chained = await AddLogAsync(ctx, "already chained");

        // Back to the state before the upgrade: no triggers, entries without chain columns
        foreach (var trigger in new[] { "chain_access_log", "prevent_access_log_change", "prevent_access_log_truncate" })
        {
            await ctx.Database.ExecuteSqlRawAsync($"DROP TRIGGER {trigger} ON archive_worm.\"AccessLogs\"");
        }
        var old1 = await AddLogAsync(ctx, "old entry 1");
        var old2 = await AddLogAsync(ctx, "old entry 2");

        await ctx.Database.ExecuteSqlRawAsync(AuditLogProtectionSql.Core);

        var head = await ChainOfAsync(ctx, chained.Id);
        var c1 = await ChainOfAsync(ctx, old1.Id);
        var c2 = await ChainOfAsync(ctx, old2.Id);
        Assert.Equal(head.ChainSeq + 1, c1.ChainSeq);
        Assert.Equal(head.Hash, c1.PrevHash);
        Assert.Equal(c1.Hash, c2.PrevHash);
        Assert.Null((await VerifyAsync(ctx)).FirstBrokenSeq);

        // The protection is back in place
        Assert.Equal(RaiseException, await SqlStateOfAsync(ctx, () => ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM archive_worm.\"AccessLogs\" WHERE \"Id\" = {0}", old1.Id)));

        await scope.RollbackAsync();
    }

    [Fact]
    public void AdminScript_ContainsTheMigrationSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MailArchiver.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var script = File.ReadAllText(Path.Combine(dir!.FullName, "doc", "sql", "MigrateV2610_3-audit-log.sql"))
            .Replace("\r\n", "\n");
        Assert.Contains(AuditLogProtectionSql.Core.Replace("\r\n", "\n"), script);
    }
}
