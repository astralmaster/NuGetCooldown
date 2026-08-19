# NuGetCooldown

**Fail the build when any NuGet package version is younger than N days.**

[![NuGet](https://img.shields.io/nuget/v/NuGetCooldown?label=NuGetCooldown)](https://www.nuget.org/packages/NuGetCooldown)
[![NuGet](https://img.shields.io/nuget/v/NuGetCooldown.MSBuild?label=NuGetCooldown.MSBuild)](https://www.nuget.org/packages/NuGetCooldown.MSBuild)
[![CI](https://github.com/astralmaster/NuGetCooldown/actions/workflows/ci.yml/badge.svg)](https://github.com/astralmaster/NuGetCooldown/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Malicious package versions don't live long — they are typically detected and pulled
**within days** of publication. In [an analysis of recent supply-chain attacks][cooldown-blog],
8 out of 10 had an exploitation window of **under a week**. A *dependency cooldown* turns that
around: if your build refuses any package version younger than N days, the attack window
closes before it ever reaches your machines. It is one of the highest-value, lowest-cost
supply-chain defenses available — pnpm, uv, Dependabot, and Renovate all added it.
NuGet [has an open request for it][nuget-issue]; **NuGetCooldown gives it to you today, at
build time, where it actually blocks the attack.**

```text
$ nuget-cooldown check MyApp.sln

NuGetCooldown 1.0.0 — cooldown: 7 days, scope: all packages, sources: api.nuget.org
Projects: MyApp, MyApp.Tests

  x Contoso.Http 8.4.1      published 1.9 days ago; cooldown is 7 days (5.1 days remaining)  [direct; MyApp]
  ! Moq 4.20.0              the version is unlisted on its source (possibly withdrawn — check why)  [transitive; MyApp.Tests]

Checked 143 package version(s) across 2 project(s) in 1.4s, 141 from cache.
1 violation, 1 unlisted version.
```

Direct **and transitive** dependencies are checked — transitive is where these attacks hide.

## Quick start

### Enforce on every build (recommended)

Add one package — every `dotnet build` (and Visual Studio build) now enforces the cooldown:

```xml
<PackageReference Include="NuGetCooldown.MSBuild" Version="1.0.0" PrivateAssets="all" />
```

To cover **every project in your repo**, put that line in a top-level `Directory.Build.props`:

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="NuGetCooldown.MSBuild" Version="1.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

A too-young package now fails the build with a normal MSBuild error:

```text
C:\repo\MyApp.csproj : error NCD001: Package Contoso.Http 8.4.1 published 1.9 days ago;
cooldown is 7 days (5.1 days remaining) [direct]
```

The check is **incremental**: after a clean result it is skipped entirely until your
dependency graph or configuration changes, so day-to-day builds pay nothing.

### Or run it as a CLI

```bash
dotnet tool install --global NuGetCooldown

nuget-cooldown check                      # current directory (solution/projects auto-discovered)
nuget-cooldown check MyApp.sln --days 14
nuget-cooldown check --format json        # machine-readable, for CI
nuget-cooldown info Newtonsoft.Json 13.0.3
```

Run it against anything that has been restored — it reads `project.assets.json`, so the
full resolved graph is exactly what your build uses.

## Configuration

Put `nuget-cooldown.json` next to your solution (found automatically by walking up from the
project, like `.editorconfig`). Comments and trailing commas are allowed.

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/astralmaster/NuGetCooldown/main/nuget-cooldown.schema.json",
  "cooldownDays": 7,
  "scope": "all",                          // or "direct"
  "allow": [
    "MyCompany.*",                         // our own packages: any version
    "Serilog@4.3.0"                        // one specific version (reviewed by hand)
  ],
  "sources": [ "https://api.nuget.org/v3/index.json" ],
  "onUnknown": "warn",                     // publish date undeterminable: warn | error | ignore
  "onUnlisted": "warn",                    // version was unlisted/withdrawn: warn | error | ignore
  "onFeedError": "warn"                    // source unreachable: warn | error | ignore
}
```

Command-line flags override the config file; allow-list entries accumulate.

### MSBuild properties

Everything is also settable per project, in `Directory.Build.props`, or on the command line
(`dotnet build -p:NuGetCooldownDays=14`):

| Property | Default | Meaning |
|---|---|---|
| `NuGetCooldownEnabled` | `true` | Turn the build check off entirely |
| `NuGetCooldownDays` | `7` | Cooldown window in days |
| `NuGetCooldownScope` | `all` | `all` (incl. transitive) or `direct` |
| `NuGetCooldownWarnOnly` | `false` | Report as warnings; never fail the build |
| `NuGetCooldownAllow` | – | `;`-separated allow patterns (`MyCompany.*;Serilog@4.3.0`) |
| `NuGetCooldownSources` | nuget.org | `;`-separated NuGet V3 service index URLs |
| `NuGetCooldownOnUnknown` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownOnUnlisted` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownOnFeedError` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownConfigFile` | auto | Explicit path to `nuget-cooldown.json` |
| `NuGetCooldownNoConfig` | `false` | Ignore config files |
| `NuGetCooldownOffline` | `false` | Use only the local cache; no network |
| `NuGetCooldownNoCache` | `false` | Always query the sources |
| `NuGetCooldownCacheDir` | per-user | Cache location (also `NUGET_COOLDOWN_CACHE_DIR`) |

## How it works

1. Reads the **full resolved dependency graph** — direct and transitive — from
   `project.assets.json` (accepts a directory, `.sln`, `.slnx`, a project file, or the assets
   file itself).
2. Asks the NuGet V3 **registration API** when each package version was published — one small
   gzipped request per version. For **unlisted** versions (whose registration date is a
   `1900-01-01` sentinel) it recovers the true upload time from the **catalog**, so a
   withdrawn-and-pinned package can't hide its age.
3. Publish dates are immutable, so results go into a **local disk cache that never expires** —
   repeat checks are instant and work offline. Concurrent builds are safe (atomic per-file
   writes).
4. Anything younger than the cooldown window fails the check. Unlisted versions,
   undeterminable dates, and unreachable feeds are reported under their own policies
   (warn by default — the tool fails open, so a nuget.org outage doesn't stop your team;
   set `onFeedError: "error"` for strict mode).

## Diagnostics

| Code | Meaning |
|---|---|
| `NCD001` | Package version is younger than the cooldown window |
| `NCD002` | Publish date could not be determined |
| `NCD003` | Package version is unlisted on its source (often a takedown or author pull — check why) |
| `NCD004` | A configured source could not be queried |
| `NCD005` | Invalid usage or configuration |
| `NCD999` | Unexpected internal failure |

**Exit codes:** `0` pass (or `--warn-only`) · `1` violations/policy errors · `2` bad
usage/config · `3` unexpected failure.

## CI

The MSBuild package needs no CI setup — the build itself enforces the policy. For an explicit
gate (or repos that don't want a build-time dependency):

```yaml
# GitHub Actions
- run: dotnet restore
- run: dotnet tool install --global NuGetCooldown
- run: nuget-cooldown check --days 7 --format json
```

The JSON output (`schemaVersion: 1`) carries per-package status, publish dates, ages,
projects, and a summary block — ready for artifacts or dashboards.

## FAQ

**Doesn't Dependabot already do this?** Dependabot/Renovate cooldowns only slow down *update
PRs*. Nothing stops a teammate from adding a day-old package by hand, a floating version from
resolving one, or a fresh transitive dependency from slipping in. NuGetCooldown enforces the
policy where it's binding — at build time.

**What about private feeds?** Point `sources` at any NuGet V3 feed (the tool queries them in
order). Authenticated feeds aren't supported yet — packages that only exist there surface as
`NCD002 unknown` (policy-controlled, `warn` by default), while everything that comes from
nuget.org — where the supply-chain risk lives — is still fully checked. The practical setup
for private-feed users: `--allow "MyCompany.*"` for your internal prefix.

**Does NuGetCooldown apply to itself?** Yes — deliberately. The enforcement package is a
`PackageReference` like any other, so a freshly released version of NuGetCooldown is subject
to the same cooldown (you'll see `NCD001` for it during the first week after a release).
A tool that exempted itself from its own policy would be exactly the backdoor it exists to
prevent. Allow a specific reviewed version if you need it early:
`--allow "NuGetCooldown.MSBuild@1.0.0"`.

**Won't this break my build when nuget.org is down?** No — feed errors are warnings by
default (fail-open). Strict environments can set `onFeedError: "error"`.

**What does it cost per build?** After the first run, publish dates come from the immutable
local cache, and a clean result writes a stamp that skips the check entirely until your
dependency graph or settings change. Cold run on a large solution: a few seconds.

**Is 7 days enough?** [The data][cooldown-blog] says it blocks the large majority of observed
attacks. Raise it to 14–30 if your risk tolerance demands; `cooldownDays` is one number.

**Old, pinned versions never violate, right?** Right. Violations only occur when a version
younger than N days enters your graph — updates, new packages, or floating versions. Builds
are deterministic and only get safer as packages age.

**One caveat on cached unlisted status:** the cache stores the (immutable) publish date; the
*listed* flag is only refreshed when a version is fetched anew. For a periodic fresh audit of
unlisted packages, run `nuget-cooldown check --no-cache` on a schedule.

**packages.config?** Not supported — SDK-style projects with `PackageReference` only.

## Roadmap

- Authenticated feed support (credential providers)
- `packages.lock.json` mode for pre-restore checking
- Central version-freshness report (`nuget-cooldown report`)

## Related

- [NuGet/Home#14657 — Dependency Cooldown Option][nuget-issue] (the upstream feature request)
- [We should all be using dependency cooldowns][cooldown-blog] — William Woodruff
- [Dependabot cooldown support](https://github.blog/changelog/2025-07-01-dependabot-supports-configuration-of-a-minimum-package-age/)

## License

[MIT](LICENSE) © 2026 George Andguladze

[cooldown-blog]: https://blog.yossarian.net/2025/11/21/We-should-all-be-using-dependency-cooldowns
[nuget-issue]: https://github.com/NuGet/Home/issues/14657
