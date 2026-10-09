using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace AnimeAudioCaptioner;

public partial class CaptionOverlay : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly Queue<string> _lines = new();
    public string DisplayedText => CaptionText.Text;
    public CaptionOverlay()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => ShowNext();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x08000000);
        };
    }
    public void ShowCaption(string text)
    {
        _timer.Stop(); _lines.Clear();
        foreach (var line in SplitLines(text)) _lines.Enqueue(line);
        if (!IsVisible) Show();
        var width = SystemParameters.WorkArea.Width;
        Width = Math.Min(1200, width - 40); Height = 100;
        Left = SystemParameters.WorkArea.Left + (width - Width) / 2;
        Top = SystemParameters.WorkArea.Bottom - 145;
        ShowNext();
    }
    private void ShowNext()
    {
        _timer.Stop();
        if (_lines.Count == 0) { CaptionBox.Opacity = 0; return; }
        CaptionText.Text = _lines.Dequeue();
        CaptionText.FontSize = CaptionText.Text.Length > 36 ? 23 : 28;
        CaptionBox.Opacity = 1;
        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(CaptionText.Text.Length * 0.045 + 0.7, 1.1, 3.2));
        _timer.Start();
    }
    private static IEnumerable<string> SplitLines(string text)
    {
        var remaining = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (remaining.Length > 0)
        {
            var length = Math.Min(42, remaining.Length);
            if (length < remaining.Length)
            {
                var split = remaining.LastIndexOf(' ', length - 1, length);
                if (split >= 15) length = split;
            }
            yield return remaining[..length].Trim();
            remaining = remaining[length..].TrimStart();
        }
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
}
