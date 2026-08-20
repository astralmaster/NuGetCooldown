# Security Policy

## Reporting a vulnerability

Please report security issues **privately** so they can be fixed before public disclosure.

- Preferred: open a private report through GitHub's
  [**Report a vulnerability**](https://github.com/astralmaster/NuGetCooldown/security/advisories/new)
  form (Security → Advisories). This keeps the details private and lets us coordinate a fix.
- Please do **not** open a public issue for a suspected vulnerability.

Include, if you can: the affected version, a description of the issue, and a minimal way to
reproduce it. You can expect an initial response within a few days.

## Supported versions

This project follows semantic versioning. Security fixes are made against the latest `1.x`
release.

| Version | Supported |
|---------|-----------|
| 1.x     | ✅        |
| < 1.0   | ❌        |

## Security posture of the tool itself

NuGetCooldown is a supply-chain security tool, so it holds itself to the same bar:

- **No telemetry.** It never phones home. Its only network calls are to the NuGet V3 feeds you
  configure (nuget.org by default), to read package publish dates.
- **Minimal dependencies.** The runtime depends only on `NuGet.Versioning`, to reduce its own
  supply-chain surface.
- **Untrusted input is treated as untrusted.** Package ids and versions come from
  `project.assets.json` / `packages.lock.json`; they are never used directly as file paths, and
  cache entries are verified against the package they claim to describe.
- **Fail-open by default.** A feed outage produces warnings, not silent passes; strict enforcement
  is opt-in via the `on*` policies.
- **It applies its own cooldown to itself** — the enforcement package is a normal `PackageReference`
  and gets no exemption.
