using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgctorSDK.Host.Pages.Dashboard;

public class ActorRuntimeModel : PageModel
{
    private readonly IConfiguration _configuration;

    public ActorRuntimeModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [FromQuery(Name = "runtime")]
    public string? SelectedRuntime { get; set; }

    public void OnGet()
    {
        // Restore last saved choice from appsettings.User.json when URL has no runtime query.
        if (string.IsNullOrWhiteSpace(SelectedRuntime))
        {
            SelectedRuntime = _configuration.GetValue<string>("Agctor:DefaultRuntime", "InMemory") ?? "InMemory";
        }
    }
}
