using System.Text;
using CheckerBase.Core.Results;

namespace CheckerBase.Core.Configuration;

/// <summary>
/// Configuration for output file paths and formatting.
/// </summary>
public sealed record OutputOptions
{
    /// <summary>
    /// Path for successful results.
    /// </summary>
    public required string SuccessPath { get; init; }

    /// <summary>
    /// Path for failed results. Null to skip writing failures.
    /// </summary>
    public string? FailedPath { get; init; }

    /// <summary>
    /// Path for ignored results. Null to skip writing ignored entries.
    /// </summary>
    public string? IgnoredPath { get; init; }

    /// <summary>
    /// Custom output formatter. If null, writes the original line only.
    /// Format: (originalLine, captures) => formattedOutput
    /// </summary>
    public Func<string, IReadOnlyList<Capture>, string>? Formatter { get; init; }

    /// <summary>
    /// Whether to append to existing files instead of overwriting.
    /// Default: false (overwrite)
    /// </summary>
    public bool AppendToExisting { get; init; }

    /// <summary>
    /// Default formatter: joins line with captures using " | " separator.
    /// </summary>
    internal static string DefaultFormatter(string line, IReadOnlyList<Capture> captures)
    {
        if (captures.Count == 0)
            return line;

        var builder = new StringBuilder(line.Length + captures.Count * 20);
        builder.Append(line);

        foreach (var capture in captures)
        {
            builder.Append(" | ");
            builder.Append(capture.Key);
            builder.Append(" = ");
            builder.Append(capture.Value);
        }

        return builder.ToString();
    }
}