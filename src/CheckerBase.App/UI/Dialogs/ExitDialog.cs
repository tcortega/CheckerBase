using CheckerBase.App.Configuration;
using CheckerBase.App.Services;
using CheckerBase.App.State;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CheckerBase.App.UI.Dialogs;

/// <summary>
/// Dialog shown when user tries to exit while processing is running.
/// Offers to save progress checkpoint.
/// </summary>
public sealed class ExitDialog : Dialog
{
    private readonly AppSettings _settings;
    private readonly EngineController<ComboEntry, CheckResult, HttpClient> _engineController;
    private readonly CheckpointManager _checkpointManager;
    private readonly CheckBox _exportRemainingCheckbox;

    public bool Cancelled { get; private set; } = true;

    public ExitDialog(
        AppSettings settings,
        EngineController<ComboEntry, CheckResult, HttpClient> engineController,
        CheckpointManager checkpointManager)
        : base("Exit", 60, 14)
    {
        _settings = settings;
        _engineController = engineController;
        _checkpointManager = checkpointManager;

        // Apply dark color scheme to dialog
        if (Application.Driver != null)
        {
            ColorScheme = new ColorScheme
            {
                Normal = Application.Driver.MakeAttribute(Color.White, Color.Black),
                Focus = Application.Driver.MakeAttribute(Color.Black, Color.Cyan),
                HotNormal = Application.Driver.MakeAttribute(Color.Cyan, Color.Black),
                HotFocus = Application.Driver.MakeAttribute(Color.Black, Color.Cyan),
                Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
            };
        }

        var metrics = _engineController.GetMetrics();
        var progressText = $"Progress: {metrics.ProgressPercent:F1}% ({metrics.ProcessedLines:N0} lines)";

        var messageLabel = new Label("Processing is still running.\n\n" + progressText + "\n\nWould you like to save your progress?")
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 5
        };

        _exportRemainingCheckbox = new CheckBox("Also export remaining lines to file")
        {
            X = 2,
            Y = 6,
            Checked = false
        };

        var saveExitBtn = new Button("Save & Exit", true)
        {
            X = 2,
            Y = 9
        };
        saveExitBtn.Clicked += () => OnSaveAndExit();

        var exitBtn = new Button("Exit")
        {
            X = 20,
            Y = 9
        };
        exitBtn.Clicked += () => OnExitWithoutSaving();

        var cancelBtn = new Button("Cancel")
        {
            X = 32,
            Y = 9
        };
        cancelBtn.Clicked += () => OnCancel();

        Add(messageLabel, _exportRemainingCheckbox, saveExitBtn, exitBtn, cancelBtn);
    }

    private void OnSaveAndExit()
    {
        // Stop the engine first
        _engineController.Cancel();

        // Get current byte position
        var metrics = _engineController.GetMetrics();

        // Save checkpoint
        Task.Run(async () =>
        {
            try
            {
                await _checkpointManager.SaveCheckpointAsync(metrics.ProcessedBytes);

                // Export remaining lines if requested
                if (_exportRemainingCheckbox.Checked &&
                    !string.IsNullOrEmpty(_settings.InputFilePath))
                {
                    var outputPath = Path.Combine(
                        _settings.OutputFolder,
                        $"remaining_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                    await _checkpointManager.ExportRemainingLinesAsync(
                        _settings.InputFilePath,
                        metrics.ProcessedBytes,
                        outputPath);

                    Application.MainLoop.Invoke(() =>
                    {
                        MessageBox.Query("Progress Saved",
                            $"Checkpoint saved at {metrics.ProgressPercent:F1}%\n" +
                            $"Remaining lines exported to:\n{outputPath}",
                            "OK");
                    });
                }
                else
                {
                    Application.MainLoop.Invoke(() =>
                    {
                        MessageBox.Query("Progress Saved",
                            $"Checkpoint saved at {metrics.ProgressPercent:F1}%\n" +
                            "You can resume from this point next time.",
                            "OK");
                    });
                }
            }
            catch (Exception ex)
            {
                Application.MainLoop.Invoke(() =>
                {
                    MessageBox.ErrorQuery("Error", $"Failed to save checkpoint:\n{ex.Message}", "OK");
                });
            }
            finally
            {
                Application.MainLoop.Invoke(() =>
                {
                    Cancelled = false;
                    Application.RequestStop();
                });
            }
        });
    }

    private void OnExitWithoutSaving()
    {
        _engineController.Cancel();
        Cancelled = false;
        Application.RequestStop();
    }

    private void OnCancel()
    {
        Cancelled = true;
        Application.RequestStop();
    }
}
