using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace Jot;

/// <summary>Keep each physical mouse press a single browser input.</summary>
public class JotWebView2CompositionControl : WebView2CompositionControl
{
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // WPF Control raises MouseDoubleClick as well as MouseDown for the
        // second left/right press. This SDK sends both a native DoubleClick
        // and Down, turning a word double-click into a paragraph triple-click.
        // The DoubleClick path already forwards that second press. Do not mark
        // the routed event handled: WPF must still raise the double-click event.
        // Middle/X buttons have no WPF Control double-click class handler.
        if(e.StylusDevice is null&&e.ClickCount==2&&e.ChangedButton is MouseButton.Left or MouseButton.Right)return;
        base.OnMouseDown(e);
    }
}
