namespace NuGetCooldown.Cli;

/// <summary>The <c>--help</c> output.</summary>
internal static class HelpText
{
    public const string Text = """
        nuget-cooldown — fail the build when a NuGet package version is younger than N days.

        Newly published package versions are where supply-chain attacks live: malicious
        uploads are usually detected and removed within days. A cooldown window means
        your restored dependency graph never contains a version younger than N days.

        USAGE
          nuget-cooldown check [<path>] [options]     check a directory, solution, project,
                                                      project.assets.json, or packages.lock.json
                                                      (default: .)
          nuget-cooldown info <id> <version>          show when a package version was published
          nuget-cooldown clear-cache                  delete the local publish-date cache
          nuget-cooldown --version | --help

        CHECK OPTIONS
          -d, --days <n>            cooldown window in days (default: 7)
              --hours <n>           cooldown window in hours (added to --days; use for sub-day windows)
          -c, --config <file>       config file (default: nearest nuget-cooldown.json, walking up)
              --no-config           ignore config files
          -s, --source <url>        NuGet V3 service index; repeatable or ';'-separated
                                    (default: https://api.nuget.org/v3/index.json)
              --allow <pattern>     exempt packages: 'Id', 'Id@Version', '*' wildcards;
                                    repeatable or ';'-separated (e.g. --allow "MyCompany.*")
              --scope <all|direct>  which packages to check (default: all, incl. transitive)
              --on-unknown <a>      undeterminable publish date: warn|error|ignore (default: warn)
              --on-unlisted <a>     unlisted (withdrawn) version: warn|error|ignore (default: warn)
              --on-feed-error <a>   source query failure: warn|error|ignore (default: warn)
              --on-not-restored <a> project without a dependency graph: warn|error|ignore (default: warn)
              --warn-only           report findings but always exit 0
          -f, --format <text|json> output format (default: text)
          -v, --verbose             also list packages that passed
              --offline             use only the local cache; no network
              --no-cache            bypass the local cache; always query sources
              --cache-dir <dir>     cache location (default: %LOCALAPPDATA%/NuGetCooldown,
                                    or $NUGET_COOLDOWN_CACHE_DIR)
              --msbuild <project>   emit MSBuild-canonical errors/warnings for <project>
              --stamp-file <file>   write an up-to-date stamp after a clean check

        EXIT CODES
          0  no errors (or --warn-only)
          1  cooldown violations or policy errors
          2  invalid usage or configuration
          3  unexpected failure

        EXAMPLES
          nuget-cooldown check                        check the current directory
          nuget-cooldown check MyApp.sln --days 14
          nuget-cooldown check --format json > cooldown.json
          nuget-cooldown check --allow "MyCompany.*;Serilog@4.2.0"
          nuget-cooldown info Newtonsoft.Json 13.0.3

        Config file: put nuget-cooldown.json next to your solution. Docs and JSON schema:
        https://github.com/astralmaster/NuGetCooldown
        """;
}
