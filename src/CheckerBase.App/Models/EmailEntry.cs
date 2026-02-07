namespace CheckerBase.App.Models;

/// <summary>
/// Parsed email:password entry with extracted domain.
/// </summary>
public sealed record EmailEntry(string Email, string Password, string Domain);
