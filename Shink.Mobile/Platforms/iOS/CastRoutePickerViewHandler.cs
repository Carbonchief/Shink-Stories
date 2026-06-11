using AVKit;
using Microsoft.Maui.Handlers;
using Shink.Mobile.Views;
using UIKit;

namespace Shink.Mobile.Platforms.iOS;

public sealed class CastRoutePickerViewHandler : ViewHandler<CastRoutePickerView, AVRoutePickerView>
{
    public static readonly IPropertyMapper<CastRoutePickerView, CastRoutePickerViewHandler> Mapper =
        new PropertyMapper<CastRoutePickerView, CastRoutePickerViewHandler>(ViewMapper);

    public CastRoutePickerViewHandler()
        : base(Mapper)
    {
    }

    protected override AVRoutePickerView CreatePlatformView()
    {
        return new AVRoutePickerView
        {
            ActiveTintColor = UIColor.FromRGB(244, 162, 97),
            BackgroundColor = UIColor.Clear,
            TintColor = UIColor.White
        };
    }

    protected override void ConnectHandler(AVRoutePickerView platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.PickerOpenRequested += OnPickerOpenRequested;
    }

    protected override void DisconnectHandler(AVRoutePickerView platformView)
    {
        VirtualView.PickerOpenRequested -= OnPickerOpenRequested;
        base.DisconnectHandler(platformView);
    }

    private void OnPickerOpenRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OpenFirstButton(PlatformView);
        });
    }

    private static bool OpenFirstButton(UIView view)
    {
        if (view is UIButton button)
        {
            button.SendActionForControlEvents(UIControlEvent.TouchUpInside);
            return true;
        }

        foreach (var subview in view.Subviews)
        {
            if (OpenFirstButton(subview))
            {
                return true;
            }
        }

        return false;
    }
}
