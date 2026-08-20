# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org).

## [1.1.0] - 2026-08-20

### Added

- Stale-restore detection: when a project file is newer than its dependency graph, the check warns
  that a `dotnet restore` is probably pending (diagnostic `NCD007`), so a newly added too-new
  package can't slip past a check run against a stale graph.
- `--timeout` / `timeoutSeconds` / `NuGetCooldownTimeout` — configurable per-request feed timeout.
- `--max-parallel` / `maxParallel` / `NuGetCooldownMaxParallel` — configurable lookup concurrency.
- `--quiet` / `-q` — print only findings; a fully clean run produces no output. Handy for
  pre-commit hooks and CI.

### Changed

- The JSON report now includes a `staleProjects` array.

## [1.0.0] - 2026-08-20

Initial release.

### Added

- `NuGetCooldown` dotnet tool (`nuget-cooldown`) with `check`, `info`, and `clear-cache`
  commands.
- `NuGetCooldown.MSBuild` package: automatic, incremental cooldown enforcement on every
  build, configurable through `NuGetCooldown*` MSBuild properties.
- Full-graph checking (direct and transitive) from `project.assets.json`; accepts
  directories, `.sln`, `.slnx`, project files, assets files, or `packages.lock.json`.
- Pre-restore checking: falls back to a committed `packages.lock.json` when a project has
  not been restored.
- Sub-day cooldown windows via `cooldownHours` / `--hours` (added to the day count), matching
  pnpm and the NuGet cooldown spec's hour-based unit.
- Publish-date resolution via the NuGet V3 registration API, with a catalog fallback that
  recovers the true upload time of unlisted versions.
- Detection of unlisted (withdrawn) package versions.
- `nuget-cooldown.json` configuration with JSON schema: cooldown window, scope, allow list
  (glob patterns, per-version pins), custom V3 sources, and per-condition policies
  (`onUnknown`, `onUnlisted`, `onFeedError`, `onNotRestored`).
- Unrestored projects are surfaced in text, JSON, and MSBuild output and can fail the check
  via `onNotRestored`.
- Immutable local publish-date cache (offline-capable, safe under concurrent builds); cache
  paths are derived from a hash of the package identity, so a hostile assets file cannot
  cause path traversal, unbounded allocation, or cross-package collisions, and each entry is
  verified against the requested id/version on read.
- Output formats: human-readable text, JSON (`schemaVersion: 1`), and MSBuild-canonical
  diagnostics (`NCD001`–`NCD999`).

[1.1.0]: https://github.com/astralmaster/NuGetCooldown/releases/tag/v1.1.0
[1.0.0]: https://github.com/astralmaster/NuGetCooldown/releases/tag/v1.0.0
