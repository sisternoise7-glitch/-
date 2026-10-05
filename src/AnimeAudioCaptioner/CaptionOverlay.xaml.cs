using System.Windows;
using System.Windows.Threading;

namespace AnimeAudioCaptioner;

public partial class CaptionOverlay : Window
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public CaptionOverlay()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            CaptionBox.Opacity = 0;
        };
        Loaded += (_, _) => CoverScreens();
    }

    public void ShowCaption(string text)
    {
        if (!IsVisible) Show();
        CoverScreens();
        CaptionText.Text = text;
        CaptionBox.Opacity = 1;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void CoverScreens()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }
}
