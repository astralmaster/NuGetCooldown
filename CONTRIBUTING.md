# Contributing

Thanks for your interest in improving NuGetCooldown. Bug reports, feature ideas, and pull
requests are all welcome.

## Getting set up

You need the .NET SDK (10.0 or newer; the projects target net8.0 and run on .NET 8+).

```bash
git clone https://github.com/astralmaster/NuGetCooldown.git
cd NuGetCooldown
dotnet build -c Release
dotnet test  -c Release
```

The solution has three source projects and one test project:

- `src/NuGetCooldown.Core` — the engine (feed lookups, cache, policy, reporting). No CLI or MSBuild
  concerns; this is where most logic lives and where most tests point.
- `src/NuGetCooldown.Cli` — the `nuget-cooldown` dotnet tool (argument parsing, output).
- `src/NuGetCooldown.MSBuild` — packaging project that ships the build hook plus the CLI runner.
- `tests/NuGetCooldown.Tests` — unit tests, plus a few live nuget.org integration tests
  (trait `Category=Integration`).

## Running the checks locally

```bash
# Unit + integration tests
dotnet test -c Release

# End-to-end test of the packed packages against a real project
dotnet pack -c Release -o artifacts/packages
pwsh ./eng/test-packages.ps1
```

## Pull requests

- Keep changes focused; one concern per PR is easiest to review.
- Add or update tests for any behavior change. Tests should encode *why* the behavior matters, not
  just what it does.
- The build treats warnings as errors and uses nullable reference types — please keep it warning-clean.
- Match the surrounding style (file-scoped namespaces, `var` where the type is obvious, XML docs on
  public members).
- Update `README.md`, `CHANGELOG.md`, and `nuget-cooldown.schema.json` when you change user-facing
  behavior or configuration.

## Reporting bugs

Open an issue with the version you're on, the command or configuration you ran, and what you
expected versus what happened. A minimal repro (a small `project.assets.json` or a sample project)
helps a lot.

For anything security-sensitive, see [SECURITY.md](SECURITY.md) instead of opening a public issue.
