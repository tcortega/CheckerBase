namespace CheckerBase.Core.Results;

/// <summary>
/// A key-value pair representing captured data from processing.
/// </summary>
/// <param name="Key">The capture name/identifier.</param>
/// <param name="Value">The captured value.</param>
public readonly record struct Capture(string Key, object Value)
{
    /// <summary>
    /// Returns a formatted string representation: "Key = Value".
    /// </summary>
    public override string ToString() => $"{Key} = {Value}";
}