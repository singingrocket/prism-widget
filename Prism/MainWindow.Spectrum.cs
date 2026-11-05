using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Prism.Audio;

namespace Prism;

public sealed partial class MainWindow
{
    private const int SpectrumBarCount = 40;
    private const double SpectrumMinBarHeight = 2;
    private const double SpectrumSilenceLevel = 0.04;
    private const double MicrophoneGainDb = 12;
    private static readonly TimeSpan SpectrumHideDelay = TimeSpan.FromSeconds(1.5);

    private readonly AudioCapture _audio = new();
    private readonly SpectrumAnalyzer _analyzer = new(SpectrumBarCount);
    private readonly Rectangle[] _spectrumBars = new Rectangle[SpectrumBarCount];
    private DateTime _lastSound = DateTime.MinValue;
    private bool _spectrumShown;
    private bool _spectrumRendering;

    private void InitializeSpectrum()
    {
        var brush = (Brush)Application.Current.Resources["SpectrumBarBrush"];
        for (int i = 0; i < SpectrumBarCount; i++)
        {
            SpectrumStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bar = new Rectangle
            {
                Fill = brush,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = SpectrumMinBarHeight,
            };
            Grid.SetColumn(bar, i);
            SpectrumStrip.Children.Add(bar);
            _spectrumBars[i] = bar;
        }

        AppWindow.Changed += (_, args) =>
        {
            if (args.DidVisibilityChange)
            {
                ApplySpectrumSource();
            }
        };
        ApplySpectrumSource();
    }

    /// <summary>Starts or stops capture to match the setting; capture pauses while the widget is hidden.</summary>
    private void ApplySpectrumSource()
    {
        bool active = _settings.SpectrumSource != AudioSource.Off && AppWindow.IsVisible;
        _analyzer.GainDb = _settings.SpectrumSource == AudioSource.Microphone ? MicrophoneGainDb : 0;
        _audio.Start(active ? _settings.SpectrumSource : AudioSource.Off);
        if (active && !_spectrumRendering)
        {
            CompositionTarget.Rendering += OnSpectrumFrame;
            _spectrumRendering = true;
        }
        else if (!active && _spectrumRendering)
        {
            CompositionTarget.Rendering -= OnSpectrumFrame;
            _spectrumRendering = false;
            ShowSpectrum(false);
        }
    }

    private void OnSpectrumFrame(object? sender, object e)
    {
        _analyzer.Update(_audio);
        var levels = _analyzer.Levels;
        double maxHeight = SpectrumStrip.Height;
        double loudest = 0;
        for (int i = 0; i < _spectrumBars.Length; i++)
        {
            loudest = Math.Max(loudest, levels[i]);
            _spectrumBars[i].Height = Math.Max(SpectrumMinBarHeight, levels[i] * maxHeight);
        }

        if (loudest > SpectrumSilenceLevel)
        {
            _lastSound = DateTime.Now;
        }
        ShowSpectrum(DateTime.Now - _lastSound < SpectrumHideDelay);
    }

    private void ShowSpectrum(bool show)
    {
        if (show == _spectrumShown)
        {
            return;
        }
        _spectrumShown = show;
        SpectrumStrip.Opacity = show ? 1 : 0;
        if (show)
        {
            SpectrumStrip.Visibility = Visibility.Visible;
            ResizeToContent();
        }
        else
        {
            // Collapse once the fade-out has finished so the card shrinks back.
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(450);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                if (!_spectrumShown)
                {
                    SpectrumStrip.Visibility = Visibility.Collapsed;
                    ResizeToContent();
                }
            };
            timer.Start();
        }
    }

    private void SpectrumItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.SpectrumSource = Enum.Parse<AudioSource>((string)((RadioMenuFlyoutItem)sender).Tag);
        _settings.Save();
        ApplySpectrumSource();
    }

    private void StopSpectrum()
    {
        if (_spectrumRendering)
        {
            CompositionTarget.Rendering -= OnSpectrumFrame;
            _spectrumRendering = false;
        }
        _audio.Dispose();
    }
}
