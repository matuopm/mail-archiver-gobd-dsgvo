using MailArchiver.Models;
namespace MailArchiver.Services
{
    public interface IAuthenticationService
    {
        bool ValidateCredentials(string username, string password);
        Task StartUserSessionAsync(User user, bool rememberMe = false);
        void SignOut(HttpContext context);
        bool IsAuthenticated(HttpContext context);
        string GetCurrentUserDisplayName(HttpContext context);
        int? GetCurrentUserId(HttpContext context);
        bool IsCurrentUserAdmin(HttpContext context);
        bool IsCurrentUserSelfManager(HttpContext context);
        /// <summary>Auditor: reads all mail accounts, may not change anything.</summary>
        bool IsCurrentUserAuditor(HttpContext context);
    }
}
