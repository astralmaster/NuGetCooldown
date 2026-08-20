# NuGetCooldown

**Fail the build when any NuGet package version is younger than N days — the supply-chain check NuGet's own cooldown design leaves out.**

[![NuGet](https://img.shields.io/nuget/v/NuGetCooldown?label=NuGetCooldown)](https://www.nuget.org/packages/NuGetCooldown)
[![NuGet](https://img.shields.io/nuget/v/NuGetCooldown.MSBuild?label=NuGetCooldown.MSBuild)](https://www.nuget.org/packages/NuGetCooldown.MSBuild)
[![CI](https://github.com/astralmaster/NuGetCooldown/actions/workflows/ci.yml/badge.svg)](https://github.com/astralmaster/NuGetCooldown/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A *dependency cooldown* (a.k.a. *minimum release age*) refuses to use any package version until it
has existed for at least N days. Malicious package versions don't live long — they are typically
detected and pulled **within days** of publication. In [an analysis of recent supply-chain
attacks][cooldown-blog], 8 out of 10 had an exploitation window of **under a week**, so even a
7-day cooldown would have blocked most of them from ever reaching a build.

NuGet is [adding a native cooldown][nuget-issue], but its [accepted V1 spec][nuget-spec] is
**update-time only** — it gates `dotnet package update` and Visual Studio's version picker, and
[explicitly does **not** check during restore/build][nuget-spec] (the team's prototype could not
keep restore fast enough). In their own words: *"customers who hand edit MSBuild XML and run
restore will not get benefit from the cooldown feature initially."*

**That build-time gap is exactly what NuGetCooldown fills.** It enforces the cooldown against your
fully resolved dependency graph — direct **and** transitive — every time you build, so a too-new
package can never slip in through a hand-edited version, a floating range, or a fresh transitive
dependency.

```text
$ nuget-cooldown check MyApp.sln

NuGetCooldown 1.0.0 — cooldown: 7 days, scope: all packages, sources: api.nuget.org
Projects: MyApp, MyApp.Tests

  x Contoso.Http 8.4.1      published 1.9 days ago; cooldown is 7 days (5.1 days remaining)  [direct; MyApp]
  ! Moq 4.20.0              the version is unlisted on its source (possibly withdrawn — check why)  [transitive; MyApp.Tests]

Checked 143 package version(s) across 2 project(s) in 1.4s, 141 from cache.
1 violation, 1 unlisted version.
```

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
nuget-cooldown check --hours 24           # sub-day windows, like pnpm's 24h default
nuget-cooldown check --format json        # machine-readable, for CI
nuget-cooldown info Newtonsoft.Json 13.0.3
```

It reads `project.assets.json` (the restored graph), and falls back to a committed
`packages.lock.json` when a project has not been restored — so it can also run **pre-restore** in
lockfile-based repos.

## Configuration

Put `nuget-cooldown.json` next to your solution (found automatically by walking up from the
project, like `.editorconfig`). Comments and trailing commas are allowed.

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/astralmaster/NuGetCooldown/main/nuget-cooldown.schema.json",
  "cooldownDays": 7,
  "cooldownHours": 0,                      // added to cooldownDays; use alone for sub-day windows
  "scope": "all",                          // or "direct"
  "allow": [
    "MyCompany.*",                         // our own packages: any version
    "Serilog@4.3.0"                        // one specific version (reviewed by hand)
  ],
  "sources": [ "https://api.nuget.org/v3/index.json" ],
  "onUnknown": "warn",                     // publish date undeterminable: warn | error | ignore
  "onUnlisted": "warn",                    // version was unlisted/withdrawn: warn | error | ignore
  "onFeedError": "warn",                   // source unreachable: warn | error | ignore
  "onNotRestored": "warn",                 // project has no graph to check: warn | error | ignore
  "timeoutSeconds": 30,                    // per-request feed timeout
  "maxParallel": 8                         // max concurrent feed lookups (1-32)
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
| `NuGetCooldownHours` | – | Extra hours added to the window (use alone for sub-day windows) |
| `NuGetCooldownScope` | `all` | `all` (incl. transitive) or `direct` |
| `NuGetCooldownWarnOnly` | `false` | Report as warnings; never fail the build |
| `NuGetCooldownAllow` | – | `;`-separated allow patterns (`MyCompany.*;Serilog@4.3.0`) |
| `NuGetCooldownSources` | nuget.org | `;`-separated NuGet V3 service index URLs |
| `NuGetCooldownOnUnknown` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownOnUnlisted` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownOnFeedError` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownOnNotRestored` | `warn` | `warn` \| `error` \| `ignore` |
| `NuGetCooldownTimeout` | `30` | Per-request feed timeout, in seconds |
| `NuGetCooldownMaxParallel` | `8` | Max concurrent feed lookups (1–32) |
| `NuGetCooldownConfigFile` | auto | Explicit path to `nuget-cooldown.json` |
| `NuGetCooldownNoConfig` | `false` | Ignore config files |
| `NuGetCooldownOffline` | `false` | Use only the local cache; no network |
| `NuGetCooldownNoCache` | `false` | Always query the sources |
| `NuGetCooldownCacheDir` | per-user | Cache location (also `NUGET_COOLDOWN_CACHE_DIR`) |

## How it works

1. Reads the **full resolved dependency graph** — direct and transitive — from
   `project.assets.json` (accepts a directory, `.sln`, `.slnx`, a project file, the assets file,
   or a `packages.lock.json`). When a project hasn't been restored it falls back to a committed
   lock file if present, so the check can run before restore.
2. Asks the NuGet V3 **registration API** when each package version was published — one small
   gzipped request per version. For **unlisted** versions (whose registration date is a
   `1900-01-01` sentinel) it recovers the true upload time from the **catalog**, so a
   withdrawn-and-pinned package can't hide its age.
3. Publish dates are immutable, so results go into a **local disk cache that never expires** —
   repeat checks are instant and work offline. Package ids are never used as file paths (they come
   from an untrusted assets file), so the cache is safe against hostile input; concurrent builds
   write atomically.
4. Anything younger than the cooldown window fails the check. Unlisted versions, undeterminable
   dates, unreachable feeds, and unrestored projects are each reported under their own policy
   (warn by default — the tool fails open, so a nuget.org outage doesn't stop your team; set the
   relevant `on*` policy to `error` for strict mode). If a project file is newer than its
   dependency graph, it warns that a `dotnet restore` is probably pending, since the check would
   otherwise be looking at stale dependencies.

## Diagnostics

| Code | Meaning |
|---|---|
| `NCD001` | Package version is younger than the cooldown window |
| `NCD002` | Publish date could not be determined |
| `NCD003` | Package version is unlisted on its source (often a takedown or author pull — check why) |
| `NCD004` | A configured source could not be queried |
| `NCD005` | A project was requested but has no dependency graph to check (not restored) |
| `NCD006` | Invalid usage or configuration |
| `NCD007` | A project was edited after its last restore; results may be stale |
| `NCD999` | Unexpected internal failure |

**Exit codes:** `0` pass (or `--warn-only`) · `1` violations/policy errors · `2` bad
usage/config · `3` unexpected failure.

## Security patches and the cooldown

The one case where a cooldown works against you is a **security fix**: when a package you use gets
a CVE and the fixed version is still inside the window, you want it *now*, not in seven days. This
is a deliberate risk trade-off — a known-vulnerable version is riskier than a merely-new one — and
[the NuGet team flags it as the central design question][nuget-issue] for native cooldown too.

NuGetCooldown's escape hatch is the allow-list, applied to the exact reviewed version:

```bash
nuget-cooldown check --allow "Contoso.Http@8.4.2"     # take the fix immediately, this version only
```

or in `nuget-cooldown.json`:

```jsonc
{ "allow": ["Contoso.Http@8.4.2"] }
```

Because it names one version, the cooldown keeps protecting every *other* package and every future
version. Automatic CVE-aware bypass (reading advisory data to exempt fixes on its own) is on the
roadmap; today the decision is explicit and auditable, which for a security control is a feature.

## How this relates to native NuGet and other tools

- **Native NuGet cooldown** ([spec #14983][nuget-spec], accepted 2026): configured per-source in
  `nuget.config` (`minPublishAgeHours`), it gates **updates** and floating versions but not
  restore/build of pinned versions. NuGetCooldown is complementary — it's the build-time gate the
  spec leaves for later. When the native feature ships, you can keep NuGetCooldown for enforcement
  or retire it; either way your policy is expressed the same way.
- **[dotnet-pkg-age](https://github.com/jcmrva/dotnet-pkg-age)**: a similar CLI focused on
  `packages.lock.json` / `Directory.Packages.props` / `packages.config`. NuGetCooldown differs by
  checking the full transitive graph from the assets file, detecting unlisted versions, and
  shipping an automatic MSBuild hook.
- **Dependabot / Renovate cooldowns**: these only slow down *update PRs*. Nothing stops a
  teammate from adding a day-old package by hand or a floating version from resolving one.
  NuGetCooldown enforces the policy where it's binding — at build time.

## CI

The MSBuild package needs no CI setup — the build itself enforces the policy. For an explicit
gate (or repos that don't want a build-time dependency):

```yaml
# GitHub Actions
- run: dotnet restore
- run: dotnet tool install --global NuGetCooldown
- run: nuget-cooldown check --days 7 --format json
```

The JSON output (`schemaVersion: 1`) carries per-package status, publish dates, ages, projects,
and a summary block — ready for artifacts or dashboards.

## FAQ

**Won't this break my build when nuget.org is down?** No — feed errors are warnings by default
(fail-open). Strict environments can set `onFeedError: "error"`.

**Does NuGetCooldown apply to itself?** Yes — deliberately. The enforcement package is a
`PackageReference` like any other, so a freshly released version is subject to the same cooldown.
A tool that exempted itself from its own policy would be exactly the backdoor it exists to prevent.
Allow a specific reviewed version if you need it early: `--allow "NuGetCooldown.MSBuild@1.0.0"`.

**What does it cost per build?** After the first run, publish dates come from the immutable local
cache, and a clean result writes a stamp that skips the check entirely until your dependency graph
or settings change. Cold run on a large solution: a few seconds.

**Is 7 days enough?** [The data][cooldown-blog] says it blocks the large majority of observed
attacks. Raise it with `cooldownDays`, or use `cooldownHours` for a sub-day window (pnpm defaults
to 24h). Both units add together.

**What about private feeds?** Point `sources` at any NuGet V3 feed (queried in order).
Authenticated feeds aren't supported yet — packages that only exist there surface as `NCD002`
(policy-controlled, `warn` by default), while everything from nuget.org is fully checked. The
practical setup: `--allow "MyCompany.*"` for your internal prefix.

**Old, pinned versions never violate, right?** Right. Violations only occur when a version younger
than the window enters your graph — updates, new packages, or floating versions. Builds are
deterministic and only get safer as packages age.

**One caveat on cached unlisted status:** the cache stores the (immutable) publish date; the
*listed* flag is only refreshed when a version is fetched anew. For a periodic fresh audit of
unlisted packages, run `nuget-cooldown check --no-cache` on a schedule.

**packages.config?** SDK-style projects with `PackageReference` (assets file or lock file) only.

## Roadmap

- CVE-aware automatic bypass (read advisory data to exempt security fixes)
- Authenticated feed support (credential providers)
- Per-source cooldown windows (trust internal feeds without an allow-list)

## Related

- [NuGet/Home#14657 — Dependency Cooldown Option][nuget-issue] (the upstream feature request)
- [NuGet/Home#14983 — accepted "Package update cooldown V1" spec][nuget-spec]
- [We should all be using dependency cooldowns][cooldown-blog] — William Woodruff
- [Dependabot cooldown support](https://github.blog/changelog/2025-07-01-dependabot-supports-configuration-of-a-minimum-package-age/)

## License

[MIT](LICENSE) © 2026 George Andguladze

[cooldown-blog]: https://blog.yossarian.net/2025/11/21/We-should-all-be-using-dependency-cooldowns
[nuget-issue]: https://github.com/NuGet/Home/issues/14657
[nuget-spec]: https://github.com/NuGet/Home/pull/14983
