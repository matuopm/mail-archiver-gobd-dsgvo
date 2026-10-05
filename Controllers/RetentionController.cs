using MailArchiver.Attributes;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace MailArchiver.Controllers
{
    /// <summary>
    /// Retention overview for administrators: when stored originals expire and whether the
    /// automatic deletion after the retention period is paused (e.g. during a tax audit).
    /// </summary>
    [AdminRequired]
    public class RetentionController : Controller
    {
        private readonly MailArchiverDbContext _context;
        private readonly IAuthenticationService _authService;
        private readonly IAccessLogService _accessLogService;
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly ILogger<RetentionController> _logger;

        public RetentionController(MailArchiverDbContext context, IAuthenticationService authService,
            IAccessLogService accessLogService, IStringLocalizer<SharedResource> localizer,
            ILogger<RetentionController> logger)
        {
            _context = context;
            _authService = authService;
            _accessLogService = accessLogService;
            _localizer = localizer;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            ViewBag.OriginalCount = await _context.ArchivedEmailSources.CountAsync();
            ViewBag.NextExpiry = await _context.ArchivedEmailSources
                .Where(s => s.RetainUntil > now)
                .MinAsync(s => (DateTime?)s.RetainUntil);
            ViewBag.ExpiredCount = await _context.ArchivedEmailSources.CountAsync(s => s.RetainUntil <= now);
            ViewBag.WithoutOriginalCount = await _context.ArchivedEmails
                .CountAsync(e => !_context.ArchivedEmailSources.Any(s => s.ArchivedEmailId == e.Id));

            var holds = await _context.RetentionHolds.OrderByDescending(h => h.StartedAt).ToListAsync();
            return View(holds);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pause(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = _localizer["RetentionPauseReasonRequired"].Value;
                return RedirectToAction(nameof(Index));
            }

            if (await _context.RetentionHolds.AnyAsync(h => h.EndedAt == null))
            {
                return RedirectToAction(nameof(Index));
            }

            var username = _authService.GetCurrentUserDisplayName(HttpContext) ?? "unknown";
            _context.RetentionHolds.Add(new RetentionHold
            {
                StartedAt = DateTime.UtcNow,
                StartedBy = username,
                Reason = reason.Trim()
            });
            await _context.SaveChangesAsync();
            await _accessLogService.LogAccessAsync(username, AccessLogType.DeletionPolicy,
                searchParameters: $"Automatic deletion paused: {reason.Trim()}");
            _logger.LogInformation("Automatic deletion paused by {User}: {Reason}", username, reason.Trim());

            TempData["SuccessMessage"] = _localizer["RetentionPaused"].Value;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resume()
        {
            var active = await _context.RetentionHolds.Where(h => h.EndedAt == null).ToListAsync();
            if (active.Count == 0)
            {
                return RedirectToAction(nameof(Index));
            }

            var username = _authService.GetCurrentUserDisplayName(HttpContext) ?? "unknown";
            foreach (var hold in active)
            {
                hold.EndedAt = DateTime.UtcNow;
                hold.EndedBy = username;
            }
            await _context.SaveChangesAsync();
            await _accessLogService.LogAccessAsync(username, AccessLogType.DeletionPolicy,
                searchParameters: "Automatic deletion resumed");
            _logger.LogInformation("Automatic deletion resumed by {User}", username);

            TempData["SuccessMessage"] = _localizer["RetentionResumed"].Value;
            return RedirectToAction(nameof(Index));
        }
    }
}
