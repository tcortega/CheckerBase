using CheckerBase.Core.Results;

namespace CheckerBase.Core.Engine;

/// <summary>
/// Represents a processed entry ready for output.
/// </summary>
/// <param name="Type">The result type determining which output file to write to.</param>
/// <param name="OriginalLine">The original line from the input file.</param>
/// <param name="Captures">Captured key-value pairs from processing.</param>
public readonly record struct OutputEntry(
    ResultType Type,
    string OriginalLine,
    IReadOnlyList<Capture> Captures);