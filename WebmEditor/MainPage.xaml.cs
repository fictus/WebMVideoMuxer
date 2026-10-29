using Microsoft.AspNetCore.Components.WebView;

namespace WebmEditor;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void OnBlazorInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
#if WINDOWS
        // Lets the web page load the user's local files (video/audio) straight from their folders.
        // CoreWebView2 is read lazily (at call time) because it may not be ready when this event fires.
        var webView = e.WebView;
        MediaHost.MapFolder = (host, folder) =>
        {
            var core = webView.CoreWebView2 ?? throw new InvalidOperationException("CoreWebView2 is not ready.");
            core.SetVirtualHostNameToFolderMapping(
                host, folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        };
#endif
    }
}
