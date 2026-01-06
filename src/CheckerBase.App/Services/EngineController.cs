using CheckerBase.App.Configuration;
using CheckerBase.App.State;
using CheckerBase.Core.Configuration;
using CheckerBase.Core.Engine;
using CheckerBase.Core.Metrics;
using CheckerBase.Core.Proxies;

namespace CheckerBase.App.Services;

/// <summary>
/// Engine state enumeration.
/// </summary>
public enum EngineState
{
    Idle,
    Running,
    Paused,
    Completed,
    Cancelled,
    Error
}

/// <summary>
/// Event arguments for engine state changes.
/// </summary>
public sealed class EngineStateChangedEventArgs : EventArgs
{
    public required EngineState NewState { get; init; }
    public Exception? Error { get; init; }
}

/// <summary>
/// Controls the CheckerEngine lifecycle and provides UI-friendly interface.
/// </summary>
/// <typeparam name="TLogEntry">The log entry type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
/// <typeparam name="TClient">The client type.</typeparam>
public sealed class EngineController<TLogEntry, TResult, TClient> : IDisposable
    where TClient : IDisposable
{
    private readonly IChecker<TLogEntry, TResult, TClient> _checker;
    private readonly AppSettings _settings;
    private readonly CheckpointManager _checkpointManager;

    private CheckerEngine<TLogEntry, TResult, TClient>? _engine;
    private ProxyRotator? _proxyRotator;
    private Task? _runTask;
    private CancellationTokenSource? _cts;
    private string? _tempFilePath;
    private bool _disposed;

    /// <summary>
    /// Current engine state.
    /// </summary>
    public EngineState State { get; private set; } = EngineState.Idle;

    /// <summary>
    /// Whether the engine is currently running (including paused).
    /// </summary>
    public bool IsRunning => State is EngineState.Running or EngineState.Paused;

    /// <summary>
    /// Whether the engine is paused.
    /// </summary>
    public bool IsPaused => State == EngineState.Paused;

    /// <summary>
    /// Number of loaded proxies (0 if none).
    /// </summary>
    public int ProxyCount => _proxyRotator?.Count ?? 0;

    /// <summary>
    /// Event raised when engine state changes.
    /// </summary>
    public event EventHandler<EngineStateChangedEventArgs>? StateChanged;

    public EngineController(
        IChecker<TLogEntry, TResult, TClient> checker,
        AppSettings settings,
        CheckpointManager checkpointManager)
    {
        _checker = checker;
        _settings = settings;
        _checkpointManager = checkpointManager;
    }

    /// <summary>
    /// Initializes the engine with current settings.
    /// </summary>
    public async Task<InitializationResult> InitializeAsync()
    {
        if (State != EngineState.Idle)
            throw new InvalidOperationException("Engine must be in Idle state to initialize");

        var errors = new List<string>();

        // Validate input file
        if (string.IsNullOrWhiteSpace(_settings.InputFilePath))
        {
            errors.Add("Input file path is required");
        }
        else if (!File.Exists(_settings.InputFilePath))
        {
            errors.Add($"Input file not found: {_settings.InputFilePath}");
        }
        else
        {
            var fileInfo = new FileInfo(_settings.InputFilePath);
            if (fileInfo.Length == 0)
                errors.Add("Input file is empty");
        }

        // Validate output folder
        if (string.IsNullOrWhiteSpace(_settings.OutputFolder))
        {
            errors.Add("Output folder is required");
        }
        else
        {
            try
            {
                Directory.CreateDirectory(_settings.OutputFolder);

                // Test write permission
                var testFile = Path.Combine(_settings.OutputFolder, ".write_test");
                await File.WriteAllTextAsync(testFile, "");
                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add($"No write permission for output folder: {_settings.OutputFolder}");
            }
            catch (IOException ex)
            {
                errors.Add($"Cannot access output folder: {ex.Message}");
            }
        }

        // Validate settings
        if (_settings.DegreeOfParallelism < 1)
            errors.Add("Degree of parallelism must be at least 1");

        if (_settings.MaxRetries < 0)
            errors.Add("Max retries cannot be negative");

        // Load proxies if specified
        int proxySuccessCount = 0;
        int proxyFailedCount = 0;

        if (!string.IsNullOrWhiteSpace(_settings.ProxyFilePath))
        {
            if (!File.Exists(_settings.ProxyFilePath))
            {
                errors.Add($"Proxy file not found: {_settings.ProxyFilePath}");
            }
            else
            {
                try
                {
                    var result = await ProxyLoader.LoadFromFileAsync(
                        _settings.ProxyFilePath,
                        defaultType: _settings.ProxyType);

                    _proxyRotator = result.Rotator;
                    proxySuccessCount = result.SuccessCount;
                    proxyFailedCount = result.FailedCount;

                    if (result.SuccessCount == 0)
                        errors.Add("No valid proxies found in proxy file");
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to load proxies: {ex.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            return new InitializationResult
            {
                Success = false,
                Errors = errors,
                ProxySuccessCount = proxySuccessCount,
                ProxyFailedCount = proxyFailedCount
            };
        }

        // Create engine
        var options = new CheckerOptions
        {
            DegreeOfParallelism = _settings.DegreeOfParallelism,
            MaxRetries = _settings.MaxRetries
        };

        var outputOptions = new OutputOptions
        {
            SuccessPath = Path.Combine(_settings.OutputFolder, "success.txt"),
            FailedPath = Path.Combine(_settings.OutputFolder, "failed.txt"),
            IgnoredPath = null, // Don't write ignored lines
            AppendToExisting = true // Append for resume support
        };

        _engine = new CheckerEngine<TLogEntry, TResult, TClient>(
            _checker, options, outputOptions, _proxyRotator);

        return new InitializationResult
        {
            Success = true,
            Errors = [],
            ProxySuccessCount = proxySuccessCount,
            ProxyFailedCount = proxyFailedCount
        };
    }

    /// <summary>
    /// Starts processing.
    /// </summary>
    /// <param name="resumeFromByte">Optional byte position to resume from.</param>
    public async Task StartAsync(long resumeFromByte = 0)
    {
        if (_engine == null)
            throw new InvalidOperationException("Engine not initialized. Call InitializeAsync first.");

        if (State != EngineState.Idle)
            throw new InvalidOperationException("Engine must be in Idle state to start");

        _cts = new CancellationTokenSource();
        var inputPath = _settings.InputFilePath!;

        // Handle resume by creating temp file with remaining content
        if (resumeFromByte > 0)
        {
            _tempFilePath = await _checkpointManager.CreateResumeTempFileAsync(inputPath, resumeFromByte);
            inputPath = _tempFilePath;
        }

        SetState(EngineState.Running);

        _runTask = Task.Run(async () =>
        {
            try
            {
                await _engine.RunAsync(inputPath, _cts.Token);

                // Clear checkpoint on successful completion
                await _checkpointManager.ClearCheckpointAsync();

                SetState(EngineState.Completed);
            }
            catch (OperationCanceledException)
            {
                SetState(EngineState.Cancelled);
            }
            catch (Exception ex)
            {
                SetState(EngineState.Error, ex);
            }
            finally
            {
                CleanupTempFile();
            }
        });
    }

    /// <summary>
    /// Pauses processing.
    /// </summary>
    public void Pause()
    {
        if (State != EngineState.Running)
            return;

        _engine?.Pause();
        SetState(EngineState.Paused);
    }

    /// <summary>
    /// Resumes processing after pause.
    /// </summary>
    public void Resume()
    {
        if (State != EngineState.Paused)
            return;

        _engine?.Resume();
        SetState(EngineState.Running);
    }

    /// <summary>
    /// Toggles pause state.
    /// </summary>
    public void TogglePause()
    {
        if (State == EngineState.Running)
            Pause();
        else if (State == EngineState.Paused)
            Resume();
    }

    /// <summary>
    /// Cancels processing.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _engine?.Cancel();
    }

    /// <summary>
    /// Gets current metrics snapshot.
    /// </summary>
    public MetricsSnapshot GetMetrics()
    {
        return _engine?.Metrics.GetSnapshot() ?? default;
    }

    /// <summary>
    /// Waits for the engine to complete.
    /// </summary>
    public async Task WaitForCompletionAsync()
    {
        if (_runTask != null)
            await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>
    /// Resets the controller to idle state for a new run.
    /// </summary>
    public void Reset()
    {
        if (IsRunning)
            throw new InvalidOperationException("Cannot reset while running");

        _engine = null;
        _proxyRotator = null;
        _runTask = null;
        _cts?.Dispose();
        _cts = null;
        CleanupTempFile();

        SetState(EngineState.Idle);
    }

    private void SetState(EngineState state, Exception? error = null)
    {
        State = state;
        StateChanged?.Invoke(this, new EngineStateChangedEventArgs
        {
            NewState = state,
            Error = error
        });
    }

    private void CleanupTempFile()
    {
        if (_tempFilePath != null && File.Exists(_tempFilePath))
        {
            try
            {
                File.Delete(_tempFilePath);
            }
            catch
            {
                // Ignore cleanup errors
            }

            _tempFilePath = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cancel();
        _cts?.Dispose();
        CleanupTempFile();
    }
}

/// <summary>
/// Result of engine initialization.
/// </summary>
public sealed class InitializationResult
{
    public required bool Success { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public int ProxySuccessCount { get; init; }
    public int ProxyFailedCount { get; init; }
}
