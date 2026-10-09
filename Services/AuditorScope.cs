using MailArchiver.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.Services
{
    /// <summary>
    /// What a restricted auditor may see during one request: the assigned mailboxes (null =
    /// all) and the audit period (dates inclusive, null = open). Stored in
    /// <see cref="HttpContext.Items"/> by <see cref="AuditorScopeMiddleware"/> and applied by
    /// the global query filters of <see cref="MailArchiverDbContext"/> to every email,
    /// attachment, original and mail account read in that request, also via REST API and MCP.
    /// </summary>
    public sealed record AuditorScope(List<int>? AccountIds, DateTime? FromDate, DateTime? ToDate)
    {
        public const string ItemKey = "MailArchiver.AuditorScope";

        public static AuditorScope? Get(HttpContext? context) =>
            context?.Items.TryGetValue(ItemKey, out var value) == true ? value as AuditorScope : null;

        /// <summary>The scope of an auditor, or null when nothing is limited.</summary>
        public static async Task<AuditorScope?> LoadAsync(MailArchiverDbContext db, string username)
        {
            var user = await db.Users.AsNoTracking()
                .Where(u => u.Username == username && u.IsAuditor)
                .Select(u => new { u.Id, u.AuditFromDate, u.AuditToDate })
                .FirstOrDefaultAsync();
            if (user == null)
                return null;

            var accountIds = await db.UserMailAccounts.AsNoTracking()
                .Where(uma => uma.UserId == user.Id)
                .Select(uma => uma.MailAccountId)
                .ToListAsync();

            if (accountIds.Count == 0 && user.AuditFromDate == null && user.AuditToDate == null)
                return null;

            return new AuditorScope(accountIds.Count == 0 ? null : accountIds, user.AuditFromDate?.Date, user.AuditToDate?.Date);
        }
    }

    /// <summary>
    /// Puts the scope of a restricted auditor into the request, after authentication.
    /// Runs for web, REST API and MCP requests alike.
    /// </summary>
    public class AuditorScopeMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditorScopeMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, IAuthenticationService authService, MailArchiverDbContext db)
        {
            if (authService.IsCurrentUserAuditor(context) && !authService.IsCurrentUserAdmin(context))
            {
                var scope = await AuditorScope.LoadAsync(db, authService.GetCurrentUserDisplayName(context));
                if (scope != null)
                {
                    context.Items[AuditorScope.ItemKey] = scope;
                }
            }

            await _next(context);
        }
    }
}
