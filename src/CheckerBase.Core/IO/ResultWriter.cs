using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using CheckerBase.Core.Configuration;
using CheckerBase.Core.Engine;
using CheckerBase.Core.Results;

namespace CheckerBase.Core.IO;

/// <summary>
/// High-performance result writer using drain pattern with periodic flushing.
/// Single-threaded consumer - no synchronization needed on internal state.
/// </summary>
public sealed class ResultWriter : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    
    private const int IntervalCheckFrequency = 100;

    private readonly OutputOptions _options;
    private readonly int _bufferSize;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxBatchSize;
    private readonly Func<string, IReadOnlyList<Capture>, string>? _customFormatter;
    
    private StreamWriter? _successWriter;
    private StreamWriter? _failedWriter;
    private StreamWriter? _ignoredWriter;
    
    private int _pendingSuccessCount;
    private int _pendingFailedCount;
    private int _pendingIgnoredCount;
    
    private long _lastFlushTimestamp;
    
    private long _droppedEntryCount;
    private long _totalEntriesWritten;
    private long _totalFlushCount;

    private bool _initialized;

    /// <summary>Number of entries dropped due to missing writer configuration.</summary>
    public long DroppedEntryCount => Volatile.Read(ref _droppedEntryCount);

    /// <summary>Total entries successfully written across all outputs.</summary>
    public long TotalEntriesWritten => Volatile.Read(ref _totalEntriesWritten);

    /// <summary>Total flush operations performed.</summary>
    public long TotalFlushCount => Volatile.Read(ref _totalFlushCount);

    public ResultWriter(
        OutputOptions options,
        int bufferSize = 65536,
        TimeSpan? flushInterval = null,
        int maxBatchSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.SuccessPath);

        _options = options;
        _bufferSize = bufferSize;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        _maxBatchSize = maxBatchSize;
        _customFormatter = options.Formatter;
    }

    private int TotalPendingCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pendingSuccessCount + _pendingFailedCount + _pendingIgnoredCount;
    }

    /// <summary>
    /// Runs the writer loop, draining batches from the channel until completion.
    /// </summary>
    public async Task RunAsync(ChannelReader<OutputEntry> reader, CancellationToken ct)
    {
        EnsureWritersInitialized();
        _lastFlushTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await ProcessWithPeriodicFlushAsync(reader, ct).ConfigureAwait(false);
        }
        finally
        {
            if (TotalPendingCount > 0)
            {
                await FlushAllAsync(ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Uses Task.WhenAny to wait for either new data OR timer expiration.
    /// This prevents the "sleepy consumer" bug where data sits unflushed when the channel is idle.
    /// </summary>
    private async Task ProcessWithPeriodicFlushAsync(ChannelReader<OutputEntry> reader,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_flushInterval);
        
        Task<bool>? waitToReadTask = null;
        Task<bool>? timerTask = null;

        while (true)
        {
            waitToReadTask ??= reader.WaitToReadAsync(ct).AsTask();
            
            Task<bool> completedTask;

            if (TotalPendingCount > 0)
            {
                timerTask ??= timer.WaitForNextTickAsync(ct).AsTask();
                completedTask = await Task.WhenAny(waitToReadTask, timerTask).ConfigureAwait(false);
            }
            else
            {
                completedTask = waitToReadTask;
                await completedTask.ConfigureAwait(false);
            }
            
            if (timerTask is not null && completedTask == timerTask)
            {
                bool timerActive;
                try
                {
                    timerActive = await timerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!timerActive) break;
                
                await FlushAllAsync(ct).ConfigureAwait(false);
                
                timerTask = null;
                continue;
            }

            bool dataAvailable;
            try
            {
                dataAvailable = await waitToReadTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            
            waitToReadTask = null;

            if (!dataAvailable) break;

            await DrainAvailableAsync(reader, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask DrainAvailableAsync(ChannelReader<OutputEntry> reader, CancellationToken ct)
    {
        var itemsProcessed = 0;

        while (reader.TryRead(out var entry))
        {
            await WriteEntryAsync(entry).ConfigureAwait(false);
            itemsProcessed++;

            if (TotalPendingCount >= _maxBatchSize)
            {
                await FlushAllAsync(ct).ConfigureAwait(false);
                itemsProcessed = 0;
                continue;
            }

            if (itemsProcessed < IntervalCheckFrequency || !ShouldFlushByInterval()) continue;

            await FlushAllAsync(ct).ConfigureAwait(false);
            itemsProcessed = 0;
        }
        
        if (TotalPendingCount > 0 && ShouldFlushByInterval())
        {
            await FlushAllAsync(ct).ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldFlushByInterval()
        => Stopwatch.GetElapsedTime(_lastFlushTimestamp) >= _flushInterval;

    private void EnsureWritersInitialized()
    {
        if (_initialized)
            return;

        _successWriter = CreateWriter(_options.SuccessPath);

        if (_options.FailedPath is not null)
            _failedWriter = CreateWriter(_options.FailedPath);

        if (_options.IgnoredPath is not null)
            _ignoredWriter = CreateWriter(_options.IgnoredPath);

        _initialized = true;
    }

    private StreamWriter CreateWriter(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var isCreateMode = !_options.AppendToExisting;

        var fileStreamOptions = new FileStreamOptions
        {
            Mode = isCreateMode ? FileMode.Create : FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            BufferSize = 0,
            Options = FileOptions.SequentialScan | FileOptions.Asynchronous
        };

        var fileStream = new FileStream(path, fileStreamOptions);

        return new(
            fileStream,
            Utf8NoBom,
            bufferSize: _bufferSize,
            leaveOpen: false)
        {
            AutoFlush = false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask WriteEntryAsync(OutputEntry entry)
    {
        var writer = GetWriter(entry.Type);

        if (writer is null)
        {
            Interlocked.Increment(ref _droppedEntryCount);
            return;
        }

        var line = FormatEntry(entry);
        await writer.WriteLineAsync(line);
        IncrementPendingCount(entry.Type);
        Interlocked.Increment(ref _totalEntriesWritten);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StreamWriter? GetWriter(ResultType type) => type switch
    {
        ResultType.Success => _successWriter,
        ResultType.Failed => _failedWriter,
        ResultType.Ignored => _ignoredWriter,
        _ => null
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncrementPendingCount(ResultType type)
    {
        switch (type)
        {
            case ResultType.Success:
                _pendingSuccessCount++;
                break;
            case ResultType.Failed:
                _pendingFailedCount++;
                break;
            case ResultType.Ignored:
                _pendingIgnoredCount++;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string FormatEntry(OutputEntry entry)
    {
        // TODO: this still allocates the string
        if (_customFormatter is null)
            return entry.OriginalLine;

        return _customFormatter(entry.OriginalLine, entry.Captures);
    }

    private async ValueTask FlushAllAsync(CancellationToken ct)
    {
        _lastFlushTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _totalFlushCount);

        if (_pendingSuccessCount > 0 && _successWriter is not null)
        {
            await _successWriter.FlushAsync(ct).ConfigureAwait(false);
            _pendingSuccessCount = 0;
        }

        if (_pendingFailedCount > 0 && _failedWriter is not null)
        {
            await _failedWriter.FlushAsync(ct).ConfigureAwait(false);
            _pendingFailedCount = 0;
        }

        if (_pendingIgnoredCount > 0 && _ignoredWriter is not null)
        {
            await _ignoredWriter.FlushAsync(ct).ConfigureAwait(false);
            _pendingIgnoredCount = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_initialized)
            return;

        List<Exception>? exceptions = null;

        await DisposeWriterAsync(_successWriter, exceptions).ConfigureAwait(false);
        await DisposeWriterAsync(_failedWriter, exceptions).ConfigureAwait(false);
        await DisposeWriterAsync(_ignoredWriter, exceptions).ConfigureAwait(false);

        _successWriter = null;
        _failedWriter = null;
        _ignoredWriter = null;
        _initialized = false;

        if (exceptions is { Count: > 0 })
            throw new AggregateException("One or more errors occurred during disposal", exceptions);
    }

    private static async ValueTask DisposeWriterAsync(StreamWriter? writer, List<Exception>? exceptions)
    {
        if (writer is null)
            return;

        try
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exceptions ??= new(3);
            exceptions.Add(ex);
        }
    }
}