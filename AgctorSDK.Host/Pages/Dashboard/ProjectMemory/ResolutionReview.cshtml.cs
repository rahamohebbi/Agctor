using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgctorSDK.Host.Pages.Dashboard.ProjectMemory;

/// <summary>
/// Thin code-behind for the resolution review dashboard page. All data is loaded client-side
/// from <c>/api/project-memory/resolution/*</c> endpoints so the page stays responsive as edges
/// flip state without requiring a full postback.
/// </summary>
public sealed class ResolutionReviewModel : PageModel
{
    public void OnGet() { }
}
