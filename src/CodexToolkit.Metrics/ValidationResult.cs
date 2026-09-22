namespace CodexToolkit.Metrics;

public sealed record ValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
