# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org).

## [1.0.0] - 2026-08-19

Initial release.

### Added

- `NuGetCooldown` dotnet tool (`nuget-cooldown`) with `check`, `info`, and `clear-cache`
  commands.
- `NuGetCooldown.MSBuild` package: automatic, incremental cooldown enforcement on every
  build, configurable through `NuGetCooldown*` MSBuild properties.
- Full-graph checking (direct and transitive) from `project.assets.json`; accepts
  directories, `.sln`, `.slnx`, project files, or assets files.
- Publish-date resolution via the NuGet V3 registration API, with a catalog fallback that
  recovers the true upload time of unlisted versions.
- Detection of unlisted (withdrawn) package versions.
- `nuget-cooldown.json` configuration with JSON schema: cooldown window, scope, allow list
  (glob patterns, per-version pins), custom V3 sources, and per-condition policies
  (`onUnknown`, `onUnlisted`, `onFeedError`).
- Immutable local publish-date cache (offline-capable, safe under concurrent builds).
- Output formats: human-readable text, JSON (`schemaVersion: 1`), and MSBuild-canonical
  diagnostics (`NCD001`–`NCD999`).

[1.0.0]: https://github.com/astralmaster/NuGetCooldown/releases/tag/v1.0.0
