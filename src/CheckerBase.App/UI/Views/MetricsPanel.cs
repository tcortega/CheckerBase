using CheckerBase.Core.Metrics;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CheckerBase.App.UI.Views;

/// <summary>
/// Displays live metrics in a formatted panel.
/// </summary>
public sealed class MetricsPanel : FrameView
{
    private readonly Label _successLabel;
    private readonly Label _failedLabel;
    private readonly Label _ignoredLabel;
    private readonly Label _retriesLabel;
    private readonly Label _cpmLabel;
    private readonly Label _elapsedLabel;
    private readonly Label _linesLabel;

    public const int RequiredHeight = 5;

    public MetricsPanel() : base("Metrics")
    {
        Width = Dim.Fill();
        Height = RequiredHeight;

        // Row 1: Success, Failed, Ignored, Retries
        var successTitle = new Label("Success:") { X = 1, Y = 0 };
        _successLabel = new Label("0")
        {
            X = Pos.Right(successTitle) + 1,
            Y = 0,
            Width = 10
        };

        var failedTitle = new Label("Failed:") { X = Pos.Right(_successLabel) + 3, Y = 0 };
        _failedLabel = new Label("0")
        {
            X = Pos.Right(failedTitle) + 1,
            Y = 0,
            Width = 10
        };

        var ignoredTitle = new Label("Ignored:") { X = Pos.Right(_failedLabel) + 3, Y = 0 };
        _ignoredLabel = new Label("0")
        {
            X = Pos.Right(ignoredTitle) + 1,
            Y = 0,
            Width = 10
        };

        var retriesTitle = new Label("Retries:") { X = Pos.Right(_ignoredLabel) + 3, Y = 0 };
        _retriesLabel = new Label("0")
        {
            X = Pos.Right(retriesTitle) + 1,
            Y = 0,
            Width = 10
        };

        // Row 2: CPM, Elapsed, Lines
        var cpmTitle = new Label("CPM:") { X = 1, Y = 2 };
        _cpmLabel = new Label("0")
        {
            X = Pos.Right(cpmTitle) + 1,
            Y = 2,
            Width = 12
        };

        var elapsedTitle = new Label("Elapsed:") { X = Pos.Right(_cpmLabel) + 3, Y = 2 };
        _elapsedLabel = new Label("00:00:00")
        {
            X = Pos.Right(elapsedTitle) + 1,
            Y = 2,
            Width = 12
        };

        var linesTitle = new Label("Lines:") { X = Pos.Right(_elapsedLabel) + 3, Y = 2 };
        _linesLabel = new Label("0")
        {
            X = Pos.Right(linesTitle) + 1,
            Y = 2,
            Width = 15
        };

        Add(
            successTitle, _successLabel,
            failedTitle, _failedLabel,
            ignoredTitle, _ignoredLabel,
            retriesTitle, _retriesLabel,
            cpmTitle, _cpmLabel,
            elapsedTitle, _elapsedLabel,
            linesTitle, _linesLabel);
    }

    /// <summary>
    /// Apply color schemes after Application.Init() has been called.
    /// </summary>
    public void ApplyColors()
    {
        if (Application.Driver == null) return;

        _successLabel.ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Green, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.Green, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.Green, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.Green, Color.Black),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
        };

        _failedLabel.ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Red, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.Red, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.Red, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.Red, Color.Black),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
        };

        _ignoredLabel.ColorScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black),
            Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
        };
    }

    /// <summary>
    /// Updates all metrics labels from a snapshot.
    /// </summary>
    public void UpdateFromSnapshot(MetricsSnapshot snapshot)
    {
        _successLabel.Text = FormatNumber(snapshot.SuccessCount);
        _failedLabel.Text = FormatNumber(snapshot.FailedCount);
        _ignoredLabel.Text = FormatNumber(snapshot.IgnoredCount);
        _retriesLabel.Text = FormatNumber(snapshot.RetryCount);
        _cpmLabel.Text = FormatNumber((long)snapshot.CPM);
        _elapsedLabel.Text = snapshot.ElapsedTime.ToString(@"hh\:mm\:ss");
        _linesLabel.Text = FormatNumber(snapshot.ProcessedLines);
    }

    /// <summary>
    /// Resets all metrics to zero.
    /// </summary>
    public void Reset()
    {
        _successLabel.Text = "0";
        _failedLabel.Text = "0";
        _ignoredLabel.Text = "0";
        _retriesLabel.Text = "0";
        _cpmLabel.Text = "0";
        _elapsedLabel.Text = "00:00:00";
        _linesLabel.Text = "0";
    }

    private static string FormatNumber(long value)
    {
        return value.ToString("N0");
    }
}
