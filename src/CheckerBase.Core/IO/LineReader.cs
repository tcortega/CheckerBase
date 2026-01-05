using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;

namespace CheckerBase.Core.IO;

/// <summary>
/// High-performance line reader using PipeReader with ArrayPool for multi-segment handling.
/// </summary>
public static class LineReader
{
    /// <summary>
    /// Reads lines from a file and writes them to a channel.
    /// </summary>
    /// <param name="filePath">Path to the input file.</param>
    /// <param name="writer">Channel writer to send lines to.</param>
    /// <param name="onBytesRead">Callback invoked with bytes read after each buffer.</param>
    /// <param name="bufferSize">Buffer size for file reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ReadLinesAsync(
        string filePath,
        ChannelWriter<string> writer,
        Action<long>? onBytesRead,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
            bufferSize: bufferSize,
            leaveOpen: false));

        Exception? processingError = null;
        var isFirstRead = true;

        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                
                if (isFirstRead)
                {
                    HandleBom(ref buffer);
                    isFirstRead = false;
                }

                var consumed = buffer.Start;
                var examined = buffer.End;

                try
                {
                    if (result.IsCanceled) break;

                    while (TryReadLine(ref buffer, out var line))
                    {
                        if (!writer.TryWrite(line))
                        {
                            await writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                        }
                        
                        consumed = buffer.Start;
                    }

                    if (result.IsCompleted)
                    {
                        if (buffer.Length > 0)
                        {
                            TrimTrailingReturn(ref buffer);

                            var lastLine = GetStringFromSequence(buffer);
                            if (!writer.TryWrite(lastLine))
                            {
                                await writer.WriteAsync(lastLine, cancellationToken).ConfigureAwait(false);
                            }

                            consumed = buffer.End;
                        }

                        break;
                    }
                }
                finally
                {
                    if (onBytesRead != null)
                    {
                        var bytesProcessed = result.Buffer.Slice(0, consumed).Length;
                        if (bytesProcessed > 0)
                            onBytesRead(bytesProcessed);
                    }

                    reader.AdvanceTo(consumed, examined);
                }
            }
        }
        catch (Exception ex)
        {
            processingError = ex;
            throw;
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
            writer.Complete(processingError);
        }
    }

    private static void HandleBom(ref ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length < 3) return;
        
        var slice = buffer.Slice(0, 3);
        Span<byte> bomSpan = stackalloc byte[3];
        slice.CopyTo(bomSpan);

        if (bomSpan[0] == 0xEF && bomSpan[1] == 0xBB && bomSpan[2] == 0xBF)
        {
            buffer = buffer.Slice(3);
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out string line)
    {
        var position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            line = string.Empty;
            return false;
        }

        var lineSequence = buffer.Slice(0, position.Value);

        TrimTrailingReturn(ref lineSequence);

        line = GetStringFromSequence(lineSequence);
        
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private static void TrimTrailingReturn(ref ReadOnlySequence<byte> sequence)
    {
        if (sequence.Length <= 0) return;
        
        var lastSegment = sequence.Slice(sequence.Length - 1);
        if (lastSegment.FirstSpan[0] == (byte)'\r')
        {
            sequence = sequence.Slice(0, sequence.Length - 1);
        }
    }

    private static string GetStringFromSequence(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(sequence.FirstSpan);
        }

        // use stackalloc for small multi-segment lines to avoid ArrayPool overhead.
        if (sequence.Length <= 256)
        {
            Span<byte> stackBuffer = stackalloc byte[(int)sequence.Length];
            sequence.CopyTo(stackBuffer);
            return Encoding.UTF8.GetString(stackBuffer);
        }

        var length = (int)sequence.Length;
        var array = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            sequence.CopyTo(array);
            return Encoding.UTF8.GetString(array, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}