using System.Reflection;
using MailArchiver.Attributes;
using MailArchiver.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MailArchiver.Tests.Attributes;

/// <summary>
/// Auditors may only read: every non-GET request is refused unless the action is marked
/// [AuditorAllowed], and the restore flows are closed even for GET.
/// </summary>
public class AuditorReadOnlyFilterTests
{
    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    public void UnmarkedActions_AllowOnlyReading(string method, bool allowed) =>
        Assert.Equal(allowed, AuditorReadOnlyFilter.IsAllowedForAuditor(method, Array.Empty<object>()));

    [Fact]
    public void AuditorAllowed_OpensAPost()
    {
        Assert.True(AuditorReadOnlyFilter.IsAllowedForAuditor("POST", new object[] { new AuditorAllowedAttribute() }));
    }

    [Fact]
    public void AuditorForbidden_ClosesEvenAGet()
    {
        Assert.False(AuditorReadOnlyFilter.IsAllowedForAuditor("GET", new object[] { new AuditorForbiddenAttribute() }));
        Assert.False(AuditorReadOnlyFilter.IsAllowedForAuditor("POST",
            new object[] { new AuditorAllowedAttribute(), new AuditorForbiddenAttribute() }));
    }

    [Theory]
    [InlineData(nameof(EmailsController.Restore))]
    [InlineData(nameof(EmailsController.BatchRestore))]
    [InlineData(nameof(EmailsController.StartAsyncBatchRestoreFromAccount))]
    [InlineData(nameof(EmailsController.GetFolders))]
    public void RestoreFlows_AreForbiddenForAuditors(string action)
    {
        var getActions = typeof(EmailsController).GetMethods()
            .Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() == null)
            .ToList();
        Assert.NotEmpty(getActions);
        Assert.All(getActions, m => Assert.NotNull(m.GetCustomAttribute<AuditorForbiddenAttribute>()));
    }

    [Fact]
    public void OnlyReadingPostActions_AreOpenForAuditors()
    {
        // Every POST action an auditor may use. Adding one here needs a reason: it must not
        // change archived data, accounts, users or settings.
        var expected = new[]
        {
            "ApiKeys.Create", "ApiKeys.Revoke",
            "Auth.Login", "Auth.LoginWithOAuth", "Auth.Logout",
            "Emails.CancelSelectedEmailsExport", "Emails.ExportSelected",
            "Localization.SetLanguage",
            "Logs.VerifyChain",
            "TwoFactor.Disable", "TwoFactor.Enable", "TwoFactor.Verify",
            "Users.ChangePassword"
        };

        var open = typeof(EmailsController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
                .Where(m => AuditorReadOnlyFilter.IsAllowedForAuditor("POST",
                    t.GetCustomAttributes(true).Concat(m.GetCustomAttributes(true))))
                .Select(m => t.Name.Replace("Controller", "") + "." + m.Name))
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), open);
    }
}
