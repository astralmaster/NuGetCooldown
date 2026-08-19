using NuGetCooldown.Model;

namespace NuGetCooldown.Checking;

/// <summary>
/// Writes the incremental-build stamp used by the MSBuild integration. The stamp is only written
/// after a fully clean check, which is safe to skip on later builds: a package that has cleared
/// its cooldown only gets older, so a clean result can never turn into a violation until the
/// dependency graph (assets file) or the configuration changes — both of which are MSBuild inputs
/// that invalidate the stamp.
/// </summary>
public static class StampFile
{
    /// <summary>Writes the stamp when <paramref name="report"/> is clean; otherwise removes a stale one.</summary>
    public static void Update(CheckReport report, string stampPath)
    {
        if (report.IsClean)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
            File.WriteAllText(
                stampPath,
                $"clean at {report.CheckedAtUtc:O} by NuGetCooldown {report.ToolVersion}\n");
        }
        else if (File.Exists(stampPath))
        {
            File.Delete(stampPath);
        }
    }
}
