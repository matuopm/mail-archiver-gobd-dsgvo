using MailArchiver.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.ViewComponents
{
    /// <summary>
    /// Notice at the top of every page while the automatic deletion is paused (page
    /// Retention), so the pause is not forgotten after an audit has ended.
    /// </summary>
    public class RetentionHoldBannerViewComponent : ViewComponent
    {
        private readonly MailArchiverDbContext _context;

        public RetentionHoldBannerViewComponent(MailArchiverDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(bool showLink)
        {
            var hold = await _context.RetentionHolds
                .AsNoTracking()
                .Where(h => h.EndedAt == null)
                .OrderBy(h => h.StartedAt)
                .FirstOrDefaultAsync();
            if (hold == null)
                return Content(string.Empty);

            ViewBag.ShowLink = showLink;
            return View(hold);
        }
    }
}
