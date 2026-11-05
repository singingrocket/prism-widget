using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Prism;

/// <summary>
/// Light acrylic backdrop that stays translucent even when the window is not focused
/// (the built-in DesktopAcrylicBackdrop falls back to a solid color when inactive).
/// </summary>
internal sealed class AlwaysActiveAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private double _transparency;

    public AlwaysActiveAcrylicBackdrop(double transparency)
    {
        _transparency = Math.Clamp(transparency, 0, 1);
    }

    /// <summary>0 = frosted (most opaque), 1 = clearest glass. Applied live.</summary>
    public double Transparency
    {
        get => _transparency;
        set
        {
            _transparency = Math.Clamp(value, 0, 1);
            ApplyTransparency();
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Light,
        };

        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            FallbackColor = Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF5),
        };
        ApplyTransparency();
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    private void ApplyTransparency()
    {
        if (_controller is null)
        {
            return;
        }
        _controller.TintOpacity = (float)(0.15 * (1 - _transparency));
        _controller.LuminosityOpacity = (float)(0.55 - 0.45 * _transparency);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }
}
