namespace NuGetCooldown.Configuration;

/// <summary>A configuration file or setting value is invalid. Maps to exit code 2.</summary>
public sealed class CooldownConfigException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>The command line or input path is invalid or unusable. Maps to exit code 2.</summary>
public sealed class CooldownUsageException(string message) : Exception(message);
