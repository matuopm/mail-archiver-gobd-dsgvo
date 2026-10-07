using MailArchiver.Auth.Middlewares;

namespace MailArchiver.Auth.Extensions
{
    public static class UseAuthExtension
    {
        public static WebApplication UseAuth(this WebApplication app)
        {
            // Add our custom authentication middleware
            app.UseMiddleware<AuthenticationMiddleware>();
            // Limits a restricted auditor to the assigned mailboxes and the audit period
            app.UseMiddleware<MailArchiver.Services.AuditorScopeMiddleware>();
            app.UseAuthorization();
            return app;
        }
    }
}
