using System.Windows;

namespace Jot;

public partial class MainWindow
{
    internal static Visibility BrowserHostVisibility(bool hostVisible,WindowState hostState)=>
        hostVisible&&hostState!=WindowState.Minimized?Visibility.Visible:Visibility.Hidden;

    private void OnBrowserHostStateChanged(object? sender,EventArgs args)=>UpdateBrowserHostVisibility();

    private void UpdateBrowserHostVisibility()
    {
        if(closingPermanently||browserDisposed||Browser is null)return;
        // WPF Window.IsVisible remains true when minimized. The composition SDK
        // mirrors only its control's IsVisible into the native controller, so
        // explicitly hide it or its alpha-zero render HWND can remain over the
        // desktop. Hidden (not Collapsed) keeps the editor's measured size, caret
        // and scroll layout ready for restore. Opacity is intentionally ignored:
        // isolated offscreen tests still need rendering with host Opacity=0.
        var visibility=BrowserHostVisibility(IsVisible,WindowState);
        if(Browser.Visibility!=visibility)Browser.Visibility=visibility;
        session.RefreshWebViewInputProtection();
    }
}
