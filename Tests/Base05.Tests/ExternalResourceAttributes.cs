using Xunit;

namespace Base05.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExternalResourceFactAttribute : FactAttribute
{
    public ExternalResourceFactAttribute(string unavailableReason, params string[] requiredPaths)
    {
        Skip = ExternalResourceAvailability.ResolveSkipReason(unavailableReason, requiredPaths);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExternalResourceTheoryAttribute : TheoryAttribute
{
    public ExternalResourceTheoryAttribute(string unavailableReason, params string[] requiredPaths)
    {
        Skip = ExternalResourceAvailability.ResolveSkipReason(unavailableReason, requiredPaths);
    }
}

internal static class ExternalResourceAvailability
{
    public static string? ResolveSkipReason(string unavailableReason, IEnumerable<string> requiredPaths)
    {
        foreach (string configuredPath in requiredPaths)
        {
            string path = Environment.ExpandEnvironmentVariables(configuredPath);
            if (!Directory.Exists(path) && !File.Exists(path))
                return unavailableReason;
        }

        return null;
    }
}