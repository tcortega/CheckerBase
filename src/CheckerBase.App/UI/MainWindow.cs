using CheckerBase.App.Configuration;
using CheckerBase.App.Services;
using CheckerBase.App.State;
using CheckerBase.App.UI.Dialogs;
using CheckerBase.App.UI.Views;
using CheckerBase.Core.Engine;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CheckerBase.App.UI;

/// <summary>
/// Main application window containing all UI components.
/// </summary>
public sealed class MainWindow : Toplevel
{
    private readonly SettingsManager _settingsManager;
    private AppSettings _settings = null!;
    private CheckpointManager _checkpointManager = null!;
    private EngineController<ComboEntry, CheckResult, HttpClient>? _engineController;

    private readonly HeaderView _headerView;
    private readonly MetricsPanel _metricsPanel;
    private readonly ProgressPanel _progressPanel;
    private readonly StatusBar _statusBar;
    private readonly MenuBar _menuBar;

    private readonly Label _statusIndicator;
    private object? _metricsTimerToken;
    private bool _initialized;

    public MainWindow()
    {
        _settingsManager = new SettingsManager();

        // Create menu bar
        _menuBar = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_File", new MenuItem[]
            {
                new MenuItem("_Configure...", "", ShowStartupDialog),
                new MenuItem("_Start", "", StartProcessing, shortcut: Key.F5),
                new MenuItem("_Pause/Resume", "", TogglePause, shortcut: Key.F6),
                new MenuItem("S_top", "", StopProcessing, shortcut: Key.F7),
                null!, // Separator
                new MenuItem("E_xit", "", RequestExit, shortcut: Key.Q | Key.CtrlMask)
            }),
            new MenuBarItem("_Help", new MenuItem[]
            {
                new MenuItem("_About", "", ShowAbout)
            })
        });

        // Create views
        _headerView = new HeaderView
        {
            X = 0,
            Y = 1 // Below menu bar
        };

        _metricsPanel = new MetricsPanel
        {
            X = 0,
            Y = Pos.Bottom(_headerView)
        };

        _progressPanel = new ProgressPanel
        {
            X = 0,
            Y = Pos.Bottom(_metricsPanel)
        };

        // Status indicator (rightmost part of status bar)
        _statusIndicator = new Label("IDLE")
        {
            X = Pos.AnchorEnd(12),
            Y = 0
        };

        // Create status bar
        _statusBar = new StatusBar(new StatusItem[]
        {
            new StatusItem(Key.F1, "~F1~ Config", ShowStartupDialog),
            new StatusItem(Key.F5, "~F5~ Start", StartProcessing),
            new StatusItem(Key.F6, "~F6~ Pause", TogglePause),
            new StatusItem(Key.F7, "~F7~ Stop", StopProcessing),
            new StatusItem(Key.Q | Key.CtrlMask, "~^Q~ Quit", RequestExit)
        });

        Add(_menuBar, _headerView, _metricsPanel, _progressPanel, _statusBar);
    }

    /// <summary>
    /// Called when the window is ready.
    /// </summary>
    public override void OnLoaded()
    {
        base.OnLoaded();

        if (_initialized) return;
        _initialized = true;

        // Apply colors now that Application.Driver is available
        _metricsPanel.ApplyColors();
        _progressPanel.ApplyColors();

        // Load settings and show startup dialog
        Task.Run(async () =>
        {
            _settings = await _settingsManager.LoadAsync();
            _checkpointManager = new CheckpointManager(_settingsManager, _settings);

            Application.MainLoop.Invoke(ShowStartupDialog);
        });
    }

    private void ShowStartupDialog()
    {
        if (_engineController?.IsRunning == true)
        {
            MessageBox.ErrorQuery("Error", "Cannot change configuration while running.", "OK");
            return;
        }

        var dialog = new StartupDialog(_settings, _checkpointManager);
        Application.Run(dialog);

        if (dialog.Cancelled)
        {
            // If no settings configured yet, exit
            if (string.IsNullOrEmpty(_settings.InputFilePath))
            {
                Application.RequestStop();
                return;
            }

            return;
        }

        // Update settings
        _settings = dialog.GetSettings();
        var shouldStart = dialog.ShouldStartImmediately;

        // Save settings
        Task.Run(async () =>
        {
            await _settingsManager.SaveAsync(_settings);
        });

        // Auto-start if user clicked Start in the dialog
        if (shouldStart)
        {
            StartProcessing();
        }
    }

    private void StartProcessing()
    {
        if (_engineController?.IsRunning == true)
        {
            MessageBox.ErrorQuery("Error", "Already running.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(_settings.InputFilePath))
        {
            ShowStartupDialog();
            return;
        }

        // Reset controller if previously used
        _engineController?.Dispose();

        var checker = new ExampleChecker();
        _checkpointManager = new CheckpointManager(_settingsManager, _settings);
        _engineController = new EngineController<ComboEntry, CheckResult, HttpClient>(
            checker, _settings, _checkpointManager);

        _engineController.StateChanged += OnEngineStateChanged;

        // Reset UI
        _metricsPanel.Reset();
        _progressPanel.Reset();
        UpdateStatusIndicator(EngineState.Running);

        // Get resume position if any
        var resumePosition = _checkpointManager.GetResumePosition() ?? 0;

        Task.Run(async () =>
        {
            var initResult = await _engineController.InitializeAsync();

            if (!initResult.Success)
            {
                Application.MainLoop.Invoke(() =>
                {
                    var errors = string.Join("\n", initResult.Errors);
                    MessageBox.ErrorQuery("Initialization Error", errors, "OK");
                    UpdateStatusIndicator(EngineState.Idle);
                });
                return;
            }

            // Show proxy load results if any
            if (initResult.ProxyFailedCount > 0)
            {
                Application.MainLoop.Invoke(() =>
                {
                    MessageBox.Query("Proxies Loaded",
                        $"Loaded {initResult.ProxySuccessCount} proxies.\n{initResult.ProxyFailedCount} lines failed to parse.",
                        "OK");
                });
            }

            await _engineController.StartAsync(resumePosition);
        });

        // Start metrics timer
        StartMetricsTimer();
    }

    private void OnEngineStateChanged(object? sender, EngineStateChangedEventArgs e)
    {
        Application.MainLoop.Invoke(() =>
        {
            UpdateStatusIndicator(e.NewState);

            switch (e.NewState)
            {
                case EngineState.Completed:
                    StopMetricsTimer();
                    UpdateMetricsOnce();
                    _progressPanel.SetComplete();
                    MessageBox.Query("Complete", "Processing completed successfully!", "OK");
                    break;

                case EngineState.Cancelled:
                    StopMetricsTimer();
                    UpdateMetricsOnce();
                    break;

                case EngineState.Error:
                    StopMetricsTimer();
                    UpdateMetricsOnce();
                    MessageBox.ErrorQuery("Error", e.Error?.Message ?? "An unknown error occurred.", "OK");
                    break;
            }
        });
    }

    private void TogglePause()
    {
        if (_engineController == null)
            return;

        _engineController.TogglePause();
    }

    private void StopProcessing()
    {
        if (_engineController?.IsRunning != true)
            return;

        var result = MessageBox.Query("Stop", "Are you sure you want to stop processing?", "Yes", "No");
        if (result != 0)
            return;

        _engineController.Cancel();
    }

    private void RequestExit()
    {
        if (_engineController?.IsRunning == true)
        {
            var dialog = new ExitDialog(_settings, _engineController, _checkpointManager);
            Application.Run(dialog);

            if (dialog.Cancelled)
            {
                return;
            }
        }

        Application.RequestStop();
    }

    private void ShowAbout()
    {
        MessageBox.Query("About CheckerBase",
            "CheckerBase - High-Performance Log Processing Framework\n\n" +
            "Version: 1.0.0\n\n" +
            "A flexible framework for processing large log files\n" +
            "with parallel processing and proxy support.",
            "OK");
    }

    private void StartMetricsTimer()
    {
        _metricsTimerToken = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(500), (_) =>
        {
            if (_engineController?.IsRunning != true)
                return false;

            UpdateMetricsOnce();
            return true; // Continue timer
        });
    }

    private void StopMetricsTimer()
    {
        if (_metricsTimerToken != null)
        {
            Application.MainLoop.RemoveTimeout(_metricsTimerToken);
            _metricsTimerToken = null;
        }
    }

    private void UpdateMetricsOnce()
    {
        if (_engineController == null)
            return;

        var snapshot = _engineController.GetMetrics();
        _metricsPanel.UpdateFromSnapshot(snapshot);
        _progressPanel.UpdateProgress(snapshot.ProgressPercent, snapshot.ETA);
    }

    private void UpdateStatusIndicator(EngineState state)
    {
        var (text, color) = state switch
        {
            EngineState.Running => ("RUNNING", Color.Green),
            EngineState.Paused => ("PAUSED", Color.BrightYellow),
            EngineState.Completed => ("COMPLETE", Color.Cyan),
            EngineState.Cancelled => ("STOPPED", Color.Red),
            EngineState.Error => ("ERROR", Color.Red),
            _ => ("IDLE", Color.Gray)
        };

        _statusIndicator.Text = $"● {text}";

        if (Application.Driver != null)
        {
            _statusIndicator.ColorScheme = new ColorScheme
            {
                Normal = Application.Driver.MakeAttribute(color, Color.Black),
                Focus = Application.Driver.MakeAttribute(color, Color.Black),
                HotNormal = Application.Driver.MakeAttribute(color, Color.Black),
                HotFocus = Application.Driver.MakeAttribute(color, Color.Black),
                Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
            };
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopMetricsTimer();
            _engineController?.Dispose();
        }

        base.Dispose(disposing);
    }
}
