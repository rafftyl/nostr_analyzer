using Avalonia.Web.Blazor;

namespace NostrSandbox.Web;

public partial class App
{
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        WebAppBuilder.Configure<NostrSandbox.App>()
            .SetupWithSingleViewLifetime();
    }
}