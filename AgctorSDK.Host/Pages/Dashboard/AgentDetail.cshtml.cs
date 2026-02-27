using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgctorSDK.Host.Pages.Dashboard;

public class AgentDetailModel : PageModel
{
    public string? Id { get; set; }

    public void OnGet(string? id)
    {
        Id = id;
    }
}
