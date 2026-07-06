using AgctorSDK.Core.Rag;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgctorSDK.Host.Pages.Dashboard;

public class RagProvidersModel : PageModel
{
    private readonly IConfiguration _configuration;

    public RagProvidersModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [FromQuery(Name = "provider")]
    public string? SelectedProvider { get; set; }

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(SelectedProvider))
        {
            SelectedProvider = _configuration.GetValue<string>("Agctor:Rag:DefaultProvider", RagProviderIds.None)
                ?? RagProviderIds.None;
        }
    }
}
