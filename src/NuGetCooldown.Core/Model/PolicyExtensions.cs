namespace NuGetCooldown.Model;

/// <summary>Maps a configured <see cref="PolicyAction"/> onto the severity it produces.</summary>
public static class PolicyExtensions
{
    /// <summary>warn → Warning, error → Error, ignore → None.</summary>
    public static Severity ToSeverity(this PolicyAction action) => action switch
    {
        PolicyAction.Error => Severity.Error,
        PolicyAction.Warn => Severity.Warning,
        _ => Severity.None,
    };
}
