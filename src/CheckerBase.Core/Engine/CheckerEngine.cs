using System.Threading.Channels;
using CheckerBase.Core.Configuration;
using CheckerBase.Core.IO;
using CheckerBase.Core.Metrics;
using CheckerBase.Core.Proxies;
using CheckerBase.Core.Results;
using Microsoft.VisualStudio.Threading;

namespace CheckerBase.Core.Engine;

/// <summary>
/// The main orchestration engine for the log processing pipeline.
/// </summary>
/// <typeparam name="TLogEntry">The parsed log entry type.</typeparam>
/// <typeparam name="TResult">The result data type.</typeparam>
/// <typeparam name="TClient">The client type (must be disposable).</typeparam>
public sealed class CheckerEngine<TLogEntry, TResult, TClient>
    where TClient : IDisposable
{
    private readonly IChecker<TLogEntry, TResult, TClient> _checker;
    private readonly CheckerOptions _options;
    private readonly OutputOptions _outputOptions;
    private readonly ProxyRotator? _proxyRotator;

    private CancellationTokenSource? _cts;
    private readonly AsyncManualResetEvent _pauseEvent = new(initialState: true);

    /// <summary>
    /// Gets the metrics tracker for this engine.
    /// </summary>
    public CheckerMetrics Metrics { get; } = new();

    /// <summary>
    /// Gets whether the engine is currently paused.
    /// </summary>
    public bool IsPaused => !_pauseEvent.IsSet;

    /// <summary>
    /// Creates a new checker engine.
    /// </summary>
    /// <param name="checker">The checker implementation.</param>
    /// <param name="options">Engine configuration options.</param>
    /// <param name="outputOptions">Output configuration options.</param>
    /// <param name="proxyRotator">Optional proxy rotator.</param>
    public CheckerEngine(
        IChecker<TLogEntry, TResult, TClient> checker,
        CheckerOptions options,
        OutputOptions outputOptions,
        ProxyRotator? proxyRotator = null)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outputOptions);

        _checker = checker;
        _options = options;
        _outputOptions = outputOptions;
        _proxyRotator = proxyRotator;
    }

    /// <summary>
    /// Pauses processing. Workers will wait at the next pause checkpoint.
    /// </summary>
    public void Pause()
    {
        _pauseEvent.Reset();
        Metrics.Pause();
    }

    /// <summary>
    /// Resumes processing after a pause.
    /// </summary>
    public void Resume()
    {
        Metrics.Resume();
        _pauseEvent.Set();
    }

    /// <summary>
    /// Requests cancellation of the current run.
    /// </summary>
    public void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Runs the processing pipeline.
    /// </summary>
    /// <param name="inputPath">Path to the input file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cts = linkedCts;
        var token = _cts.Token;

        var fileInfo = new FileInfo(inputPath);
        Metrics.SetTotalBytes(fileInfo.Length);
        Metrics.Start();

        var inputChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(_options.InputChannelCapacity)
        {
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false
        });

        var outputChannel = Channel.CreateUnbounded<OutputEntry>(new()
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        var readerTask = LineReader.ReadLinesAsync(
            inputPath,
            inputChannel.Writer,
            Metrics.AddProcessedBytes,
            _options.ReadBufferSize,
            token);

        var workerTasks = new Task[_options.DegreeOfParallelism];
        for (var i = 0; i < _options.DegreeOfParallelism; i++)
        {
            workerTasks[i] = RunWorkerAsync(inputChannel.Reader, outputChannel.Writer, token);
        }

        await using var resultWriter = new ResultWriter(
            _outputOptions,
            _options.WriteBufferSize,
            _options.FlushInterval);

        var writerTask = resultWriter.RunAsync(outputChannel.Reader, token);

        try
        {
            try
            {
                await readerTask.ConfigureAwait(false);
            }
            catch
            {
                // if Reader crashes (IO/Format error), cancel workers immediately 
                // so they don't sit around waiting for data that will never come.
                await _cts.CancelAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                // explicitly close input so workers can drain and finish
                inputChannel.Writer.TryComplete();
            }

            // if a worker crashes hard, this throws -> we go to outer catch -> Cancel all.
            await Task.WhenAll(workerTasks).ConfigureAwait(false);
        }
        catch
        {
            if (!_cts.IsCancellationRequested) await _cts.CancelAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            outputChannel.Writer.TryComplete();
            try
            {
                await writerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }

            Metrics.Stop();
        }
    }

    private async Task RunWorkerAsync(
        ChannelReader<string> inputReader,
        ChannelWriter<OutputEntry> outputWriter,
        CancellationToken token)
    {
        await Task.Yield();

        try
        {
            await foreach (var line in inputReader.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (IsPaused) await _pauseEvent.WaitAsync(token).ConfigureAwait(false);

                var result = await ProcessLineAsync(line, token).ConfigureAwait(false);

                if (result.HasValue)
                {
                    outputWriter.TryWrite(result.Value);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    private async ValueTask<OutputEntry?> ProcessLineAsync(string line, CancellationToken token)
    {
        if (!_checker.QuickValidate(line.AsSpan()))
        {
            Metrics.IncrementIgnored();
            return null;
        }

        var entry = _checker.Parse(line);
        if (entry is null)
        {
            Metrics.IncrementIgnored();
            return _outputOptions.IgnoredPath is not null
                ? new OutputEntry(ResultType.Ignored, line, [])
                : null;
        }

        var retryCount = 0;
        while (true)
        {
            var proxy = _proxyRotator?.Next();

            using var client = _checker.CreateClient(proxy);
            try
            {
                var result = await _checker.ProcessAsync(entry, client, token).ConfigureAwait(false);

                switch (result.Type)
                {
                    case ResultType.Success:
                        Metrics.IncrementSuccess();
                        return new OutputEntry(ResultType.Success, line, result.Captures);

                    case ResultType.Failed:
                        Metrics.IncrementFailed();
                        return _outputOptions.FailedPath is not null
                            ? new OutputEntry(ResultType.Failed, line, result.Captures)
                            : null;

                    case ResultType.Ignored:
                        Metrics.IncrementIgnored();
                        return _outputOptions.IgnoredPath is not null
                            ? new OutputEntry(ResultType.Ignored, line, result.Captures)
                            : null;

                    case ResultType.Retry:
                        if (retryCount >= _options.MaxRetries)
                        {
                            Metrics.IncrementFailed();
                            return _outputOptions.FailedPath is not null
                                ? new OutputEntry(ResultType.Failed, line, [])
                                : null;
                        }

                        Metrics.IncrementRetry();
                        retryCount++;
                        continue;
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                if (_checker.IsTransientException(ex) && retryCount < _options.MaxRetries)
                {
                    Metrics.IncrementRetry();
                    retryCount++;
                    continue;
                }

                Metrics.IncrementFailed();
                return _outputOptions.FailedPath is not null
                    ? new OutputEntry(ResultType.Failed, line, [])
                    : null;
            }
        }
    }
}