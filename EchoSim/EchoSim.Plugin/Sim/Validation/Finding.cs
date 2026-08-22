namespace EchoSim.Sim.Validation;

public enum Severity
{
    /// Something impossible or definitively wrong.
    Error,

    /// Legal but wasteful.
    Warning,

    /// Context for reading the two above.
    Info,
}

/// One problem found by a linter.
public readonly record struct Finding(Severity Severity, string Check, string Message, string? Subject = null);

public static class FindingExtensions
{
    public static int ErrorCount(this IEnumerable<Finding> findings)
        => findings.Count(f => f.Severity == Severity.Error);

    public static int WarningCount(this IEnumerable<Finding> findings)
        => findings.Count(f => f.Severity == Severity.Warning);
}
