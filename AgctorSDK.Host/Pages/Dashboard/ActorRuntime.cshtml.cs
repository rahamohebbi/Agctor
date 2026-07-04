using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgctorSDK.Host.Pages.Dashboard;

public class ActorRuntimeModel : PageModel
{
    [FromQuery(Name = "runtime")]
    public string? SelectedRuntime { get; set; }

    public void OnGet()
    {
    }
}
