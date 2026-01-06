using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CheckerBase.App.UI.Views;

/// <summary>
/// Displays progress bar and ETA.
/// </summary>
public sealed class ProgressPanel : FrameView
{
    private readonly ProgressBar _progressBar;
    private readonly Label _percentLabel;
    private readonly Label _etaLabel;

    public const int RequiredHeight = 4;

    public ProgressPanel() : base("Progress")
    {
        Width = Dim.Fill();
        Height = RequiredHeight;

        _progressBar = new ProgressBar
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(25), // Leave room for percent and ETA
            Height = 1,
            Fraction = 0f
        };

        _percentLabel = new Label("  0.0%")
        {
            X = Pos.Right(_progressBar) + 1,
            Y = 0,
            Width = 8
        };

        var etaTitle = new Label("ETA:")
        {
            X = Pos.Right(_percentLabel) + 2,
            Y = 0
        };

        _etaLabel = new Label("--:--:--")
        {
            X = Pos.Right(etaTitle) + 1,
            Y = 0,
            Width = 10
        };

        Add(_progressBar, _percentLabel, etaTitle, _etaLabel);
    }

    /// <summary>
    /// Apply color schemes after Application.Init() has been called.
    /// </summary>
    public void ApplyColors()
    {
        if (Application.Driver == null) return;

        _progressBar.ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Cyan, Color.DarkGray),
            Focus = Application.Driver.MakeAttribute(Color.Cyan, Color.DarkGray),
            HotNormal = Application.Driver.MakeAttribute(Color.Cyan, Color.DarkGray),
            HotFocus = Application.Driver.MakeAttribute(Color.Cyan, Color.DarkGray),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
        };
    }

    /// <summary>
    /// Updates the progress display.
    /// </summary>
    /// <param name="percent">Progress percentage (0-100).</param>
    /// <param name="eta">Estimated time remaining.</param>
    public void UpdateProgress(double percent, TimeSpan? eta)
    {
        _progressBar.Fraction = (float)(percent / 100.0);
        _percentLabel.Text = $"{percent,5:F1}%";
        _etaLabel.Text = eta?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
    }

    /// <summary>
    /// Resets progress to zero.
    /// </summary>
    public void Reset()
    {
        _progressBar.Fraction = 0f;
        _percentLabel.Text = "  0.0%";
        _etaLabel.Text = "--:--:--";
    }

    /// <summary>
    /// Sets progress to complete state.
    /// </summary>
    public void SetComplete()
    {
        _progressBar.Fraction = 1f;
        _percentLabel.Text = "100.0%";
        _etaLabel.Text = "Complete";
    }
}
