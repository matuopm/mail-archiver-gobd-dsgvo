using MailArchiver.Models;
using MailArchiver.Tests.Infrastructure;
using MailArchiver.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Xunit;

namespace MailArchiver.Tests.Services;

/// <summary>
/// Notice shown on every page while the automatic deletion is paused.
/// </summary>
[Collection(TestDbFixture.CollectionName)]
public class RetentionHoldBannerTests
{
    private readonly TestDbFixture _fixture;
    public RetentionHoldBannerTests(TestDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Banner_ShowsOldestActiveHold()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        ctx.RetentionHolds.Add(new RetentionHold
        {
            StartedBy = "admin", Reason = "ended", StartedAt = DateTime.UtcNow.AddDays(-30),
            EndedAt = DateTime.UtcNow.AddDays(-20), EndedBy = "admin"
        });
        ctx.RetentionHolds.Add(new RetentionHold { StartedBy = "admin", Reason = "tax audit", StartedAt = DateTime.UtcNow.AddDays(-2) });
        await ctx.SaveChangesAsync();

        var component = new RetentionHoldBannerViewComponent(ctx);
        var result = Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync(showLink: true));
        var hold = Assert.IsType<RetentionHold>(result.ViewData!.Model);
        Assert.Equal("tax audit", hold.Reason);
        Assert.Equal(true, result.ViewData["ShowLink"]);

        await scope.RollbackAsync();
    }

    [Fact]
    public async Task Banner_EmptyWithoutActiveHold()
    {
        await using var scope = await _fixture.CreateTransactionalContextAsync();
        var ctx = scope.Context;
        ctx.RetentionHolds.Add(new RetentionHold
        {
            StartedBy = "admin", Reason = "ended", StartedAt = DateTime.UtcNow.AddDays(-30),
            EndedAt = DateTime.UtcNow.AddDays(-20), EndedBy = "admin"
        });
        await ctx.SaveChangesAsync();

        var component = new RetentionHoldBannerViewComponent(ctx);
        var result = Assert.IsType<ContentViewComponentResult>(await component.InvokeAsync(showLink: false));
        Assert.Equal(string.Empty, result.Content);

        await scope.RollbackAsync();
    }
}
