using MailArchiver.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MailArchiver.Attributes
{
    /// <summary>
    /// Marks a POST action (or a whole controller) that auditors may still use because it
    /// changes nothing in the archive: signing in and out, their own password and 2FA,
    /// language, their own API keys, downloading selected emails, checking the access log.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuditorAllowedAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a GET action that auditors may not open although it changes nothing by itself,
    /// because it only starts a changing flow (e.g. the restore form).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AuditorForbiddenAttribute : Attribute
    {
    }

    /// <summary>
    /// Global filter that keeps auditors read-only. Deny by default: every request that is
    /// not GET/HEAD is refused unless the action is marked <see cref="AuditorAllowedAttribute"/>,
    /// so actions added later (also by upstream) are closed for auditors until reviewed.
    /// </summary>
    public class AuditorReadOnlyFilter : IActionFilter
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<AuditorReadOnlyFilter> _logger;

        public AuditorReadOnlyFilter(IAuthenticationService authService, ILogger<AuditorReadOnlyFilter> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var http = context.HttpContext;
            if (!_authService.IsCurrentUserAuditor(http) || _authService.IsCurrentUserAdmin(http))
                return;

            if (IsAllowedForAuditor(http.Request.Method, context.ActionDescriptor.EndpointMetadata))
                return;

            _logger.LogWarning("Auditor {User} was refused {Method} {Path}",
                _authService.GetCurrentUserDisplayName(http), http.Request.Method, http.Request.Path);
            context.Result = new RedirectToActionResult("AccessDenied", "Auth", null);
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        public static bool IsAllowedForAuditor(string method, IEnumerable<object> endpointMetadata)
        {
            var metadata = endpointMetadata.ToList();
            if (metadata.OfType<AuditorForbiddenAttribute>().Any())
                return false;

            return HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                || metadata.OfType<AuditorAllowedAttribute>().Any();
        }
    }
}
