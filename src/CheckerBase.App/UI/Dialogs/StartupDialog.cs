using CheckerBase.App.Configuration;
using CheckerBase.App.State;
using CheckerBase.Core.Proxies;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace CheckerBase.App.UI.Dialogs;

/// <summary>
/// Startup configuration dialog for selecting files and settings.
/// </summary>
public sealed class StartupDialog : Dialog
{
    private readonly AppSettings _settings;
    private readonly CheckpointManager _checkpointManager;

    private readonly TextField _inputFileField;
    private readonly TextField _proxyFileField;
    private readonly TextField _outputFolderField;
    private readonly TextField _threadsField;
    private readonly TextField _maxRetriesField;
    private readonly RadioGroup _proxyTypeGroup;
    private readonly CheckBox _resumeCheckbox;
    private readonly Label _resumeInfoLabel;

    public bool Cancelled { get; private set; } = true;
    public bool ShouldStartImmediately { get; private set; }

    public StartupDialog(AppSettings settings, CheckpointManager checkpointManager)
        : base("Configuration", 72, 20)
    {
        _settings = settings.Clone();
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

        var y = 1;

        // Input File
        var inputLabel = new Label("Input File:") { X = 2, Y = y };
        _inputFileField = new TextField(_settings.InputFilePath ?? "")
        {
            X = 16,
            Y = y,
            Width = 38
        };
        var inputBrowseBtn = new Button("Browse")
        {
            X = 56,
            Y = y
        };
        inputBrowseBtn.Clicked += () => BrowseInputFile();

        y += 2;

        // Proxy File
        var proxyLabel = new Label("Proxy File:") { X = 2, Y = y };
        _proxyFileField = new TextField(_settings.ProxyFilePath ?? "")
        {
            X = 16,
            Y = y,
            Width = 38
        };
        var proxyBrowseBtn = new Button("Browse")
        {
            X = 56,
            Y = y
        };
        proxyBrowseBtn.Clicked += () => BrowseProxyFile();

        y += 2;

        // Output Folder
        var outputLabel = new Label("Output:") { X = 2, Y = y };
        _outputFolderField = new TextField(_settings.OutputFolder)
        {
            X = 16,
            Y = y,
            Width = 38
        };
        var outputBrowseBtn = new Button("Browse")
        {
            X = 56,
            Y = y
        };
        outputBrowseBtn.Clicked += () => BrowseOutputFolder();

        y += 2;

        // Threads and Max Retries on same line
        var threadsLabel = new Label("Threads:") { X = 2, Y = y };
        _threadsField = new TextField(_settings.DegreeOfParallelism.ToString())
        {
            X = 16,
            Y = y,
            Width = 6
        };

        var retriesLabel = new Label("Max Retries:") { X = 30, Y = y };
        _maxRetriesField = new TextField(_settings.MaxRetries.ToString())
        {
            X = 44,
            Y = y,
            Width = 6
        };

        y += 2;

        // Proxy Type
        var proxyTypeLabel = new Label("Proxy Type:") { X = 2, Y = y };
        _proxyTypeGroup = new RadioGroup(new NStack.ustring[] { "HTTP", "HTTPS", "SOCKS4", "SOCKS5" })
        {
            X = 16,
            Y = y,
            DisplayMode = DisplayModeLayout.Horizontal,
            SelectedItem = (int)_settings.ProxyType
        };

        y += 2;

        // Resume checkbox (only if checkpoint exists)
        var checkpointDescription = _checkpointManager.GetCheckpointDescription();
        _resumeCheckbox = new CheckBox("Resume from checkpoint")
        {
            X = 2,
            Y = y,
            Visible = checkpointDescription != null,
            Checked = checkpointDescription != null
        };

        _resumeInfoLabel = new Label(checkpointDescription ?? "")
        {
            X = 2,
            Y = y + 1,
            Width = Dim.Fill(2),
            Visible = checkpointDescription != null
        };

        if (Application.Driver != null)
        {
            _resumeInfoLabel.ColorScheme = new ColorScheme
            {
                Normal = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
                Focus = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
                HotNormal = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
                HotFocus = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black),
                Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
            };
        }

        y += checkpointDescription != null ? 4 : 2;

        // Buttons - properly spaced
        var startBtn = new Button("Start", true)
        {
            X = 18,
            Y = y
        };
        startBtn.Clicked += () => OnStart();

        var saveBtn = new Button("Save")
        {
            X = 32,
            Y = y
        };
        saveBtn.Clicked += () => OnSave();

        var cancelBtn = new Button("Cancel")
        {
            X = 44,
            Y = y
        };
        cancelBtn.Clicked += () => OnCancel();

        Add(
            inputLabel, _inputFileField, inputBrowseBtn,
            proxyLabel, _proxyFileField, proxyBrowseBtn,
            outputLabel, _outputFolderField, outputBrowseBtn,
            threadsLabel, _threadsField,
            retriesLabel, _maxRetriesField,
            proxyTypeLabel, _proxyTypeGroup,
            _resumeCheckbox, _resumeInfoLabel,
            startBtn, saveBtn, cancelBtn);
    }

    private void BrowseInputFile()
    {
        var dialog = new OpenDialog("Select Input File", "")
        {
            AllowsMultipleSelection = false
        };

        Application.Run(dialog);

        if (!dialog.Canceled && dialog.FilePath != null)
        {
            _inputFileField.Text = dialog.FilePath.ToString() ?? "";

            // Clear resume state if input file changed
            if (dialog.FilePath.ToString() != _settings.InputFilePath)
            {
                _resumeCheckbox.Checked = false;
                _resumeCheckbox.Visible = false;
                _resumeInfoLabel.Visible = false;
            }
        }
    }

    private void BrowseProxyFile()
    {
        var dialog = new OpenDialog("Select Proxy File", "")
        {
            AllowsMultipleSelection = false
        };

        Application.Run(dialog);

        if (!dialog.Canceled && dialog.FilePath != null)
        {
            _proxyFileField.Text = dialog.FilePath.ToString() ?? "";
        }
    }

    private void BrowseOutputFolder()
    {
        var dialog = new OpenDialog("Select Output Folder", "")
        {
            CanChooseDirectories = true,
            CanChooseFiles = false
        };

        Application.Run(dialog);

        if (!dialog.Canceled && dialog.FilePath != null)
        {
            _outputFolderField.Text = dialog.FilePath.ToString() ?? "";
        }
    }

    private bool ValidateAndApply()
    {
        // Validate input file
        var inputPath = _inputFileField.Text?.ToString()?.Trim();
        if (string.IsNullOrEmpty(inputPath))
        {
            MessageBox.ErrorQuery("Validation Error", "Input file is required.", "OK");
            _inputFileField.SetFocus();
            return false;
        }

        if (!File.Exists(inputPath))
        {
            MessageBox.ErrorQuery("Validation Error", $"Input file not found:\n{inputPath}", "OK");
            _inputFileField.SetFocus();
            return false;
        }

        // Validate threads
        if (!int.TryParse(_threadsField.Text?.ToString(), out var threads) || threads < 1)
        {
            MessageBox.ErrorQuery("Validation Error", "Threads must be a positive number.", "OK");
            _threadsField.SetFocus();
            return false;
        }

        // Validate max retries
        if (!int.TryParse(_maxRetriesField.Text?.ToString(), out var maxRetries) || maxRetries < 0)
        {
            MessageBox.ErrorQuery("Validation Error", "Max retries must be zero or positive.", "OK");
            _maxRetriesField.SetFocus();
            return false;
        }

        // Validate output folder
        var outputFolder = _outputFolderField.Text?.ToString()?.Trim();
        if (string.IsNullOrEmpty(outputFolder))
        {
            MessageBox.ErrorQuery("Validation Error", "Output folder is required.", "OK");
            _outputFolderField.SetFocus();
            return false;
        }

        // Apply settings
        _settings.InputFilePath = inputPath;
        _settings.ProxyFilePath = string.IsNullOrWhiteSpace(_proxyFileField.Text?.ToString())
            ? null
            : _proxyFileField.Text.ToString()!.Trim();
        _settings.OutputFolder = outputFolder;
        _settings.DegreeOfParallelism = threads;
        _settings.MaxRetries = maxRetries;
        _settings.ProxyType = (ProxyType)_proxyTypeGroup.SelectedItem;

        // Handle resume state
        if (!_resumeCheckbox.Checked)
        {
            _settings.ClearResumeState();
        }

        return true;
    }

    private void OnStart()
    {
        if (!ValidateAndApply())
            return;

        Cancelled = false;
        ShouldStartImmediately = true;
        Application.RequestStop();
    }

    private void OnSave()
    {
        if (!ValidateAndApply())
            return;

        Cancelled = false;
        ShouldStartImmediately = false;
        Application.RequestStop();
    }

    private void OnCancel()
    {
        Cancelled = true;
        Application.RequestStop();
    }

    /// <summary>
    /// Gets the configured settings.
    /// </summary>
    public AppSettings GetSettings() => _settings;
}
