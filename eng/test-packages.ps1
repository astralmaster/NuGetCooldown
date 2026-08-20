#!/usr/bin/env pwsh
# End-to-end test of the packed NuGetCooldown packages against a real project.
# Prerequisite: dotnet pack -c Release -o artifacts/packages
# Exercises: MSBuild auto-enforcement (pass / violation / warn-only / disabled / config file),
# incremental stamping, and the dotnet tool. Exits non-zero on the first failure.

param([string]$Version = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root 'artifacts/packages'

# When no version is passed, detect it from whatever was packed, so CI never has to track it.
if (-not $Version) {
    $pkg = Get-ChildItem (Join-Path $feed 'NuGetCooldown.MSBuild.*.nupkg') -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^NuGetCooldown\.MSBuild\.([0-9].*)\.nupkg$' } |
        Select-Object -First 1
    if ($pkg -and $pkg.Name -match '^NuGetCooldown\.MSBuild\.([0-9].*)\.nupkg$') {
        $Version = $Matches[1]
    }
}
if (-not $Version -or -not (Test-Path (Join-Path $feed "NuGetCooldown.MSBuild.$Version.nupkg"))) {
    throw "No NuGetCooldown.MSBuild package found in $feed - run 'dotnet pack -c Release -o artifacts/packages' first."
}
Write-Host "Testing packed version $Version" -ForegroundColor Cyan

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ncd-e2e-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
# Isolate from the machine: private package cache and cooldown cache.
$env:NUGET_PACKAGES = Join-Path $work 'nuget-packages'
$env:NUGET_COOLDOWN_CACHE_DIR = Join-Path $work 'cooldown-cache'

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "FAILED: $message" -ForegroundColor Red
        Write-Host "Work dir kept for inspection: $work"
        exit 1
    }
    Write-Host "  ok: $message" -ForegroundColor Green
}

function Invoke-Build([string[]]$extraArgs) {
    $output = & dotnet build (Join-Path $work 'App') -nologo @extraArgs 2>&1 | Out-String
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

Write-Host "--- Arranging sample project in $work" -ForegroundColor Cyan
@"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $work 'nuget.config') -Encoding utf8

New-Item -ItemType Directory -Force (Join-Path $work 'App') | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <!-- The enforcement package itself only exists in the local test feed. -->
    <NuGetCooldownAllow>NuGetCooldown.MSBuild</NuGetCooldownAllow>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="NuGetCooldown.MSBuild" Version="$Version" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $work 'App/App.csproj') -Encoding utf8
'public class C { }' | Set-Content (Join-Path $work 'App/C.cs') -Encoding utf8

Write-Host '--- 1. Build passes with the default 7-day cooldown' -ForegroundColor Cyan
$r = Invoke-Build @()
Assert ($r.ExitCode -eq 0) "build succeeds (exit $($r.ExitCode))"
$stamp = Get-ChildItem (Join-Path $work 'App/obj') -Recurse -Filter 'NuGetCooldown.*.stamp'
Assert ($null -ne $stamp) 'clean check wrote the incremental stamp'
$stampTime = $stamp.LastWriteTimeUtc

Write-Host '--- 2. Second build skips the check (incremental)' -ForegroundColor Cyan
$r = Invoke-Build @()
Assert ($r.ExitCode -eq 0) 'second build succeeds'
$stamp2 = Get-ChildItem (Join-Path $work 'App/obj') -Recurse -Filter 'NuGetCooldown.*.stamp'
Assert ($stamp2.LastWriteTimeUtc -eq $stampTime) 'stamp untouched, so the runner did not re-execute'

Write-Host '--- 3. Strict cooldown fails the build with NCD001' -ForegroundColor Cyan
$r = Invoke-Build @('-p:NuGetCooldownDays=3000')
Assert ($r.ExitCode -ne 0) "build fails (exit $($r.ExitCode))"
Assert ($r.Output -match 'error NCD001') 'error NCD001 reported'
Assert ($r.Output -match 'Newtonsoft\.Json') 'the offending package is named'

Write-Host '--- 3b. Hours-based window is forwarded and enforced' -ForegroundColor Cyan
# 80000 hours (~3333 days) is a valid sub-max window that Newtonsoft.Json 13.0.3 is younger than.
$r = Invoke-Build @('-p:NuGetCooldownDays=0', '-p:NuGetCooldownHours=80000')
Assert ($r.ExitCode -ne 0) "hours-based cooldown fails the build (exit $($r.ExitCode))"
Assert ($r.Output -match 'error NCD001') 'error NCD001 reported for the hours window'

Write-Host '--- 4. Warn-only reports but does not fail' -ForegroundColor Cyan
$r = Invoke-Build @('-p:NuGetCooldownDays=3000', '-p:NuGetCooldownWarnOnly=true')
Assert ($r.ExitCode -eq 0) 'build succeeds in warn-only mode'
Assert ($r.Output -match 'warning NCD001') 'warning NCD001 reported'

Write-Host '--- 5. The check can be disabled' -ForegroundColor Cyan
$r = Invoke-Build @('-p:NuGetCooldownDays=3000', '-p:NuGetCooldownEnabled=false')
Assert ($r.ExitCode -eq 0) 'build succeeds with NuGetCooldownEnabled=false'

Write-Host '--- 6. nuget-cooldown.json is discovered and applied' -ForegroundColor Cyan
'{ "cooldownDays": 3000 }' | Set-Content (Join-Path $work 'nuget-cooldown.json') -Encoding utf8
$r = Invoke-Build @()
Assert ($r.ExitCode -ne 0) 'config file cooldown fails the build'
'{ "cooldownDays": 3000, "allow": ["*"] }' | Set-Content (Join-Path $work 'nuget-cooldown.json') -Encoding utf8
$r = Invoke-Build @()
Assert ($r.ExitCode -eq 0) 'config file allow list rescues the build'
Remove-Item (Join-Path $work 'nuget-cooldown.json')

Write-Host '--- 7. The dotnet tool installs and runs from the package' -ForegroundColor Cyan
$toolDir = Join-Path $work 'tools'
Push-Location $work
try {
    & dotnet tool install --tool-path $toolDir NuGetCooldown --version $Version | Out-Null
    Assert ($LASTEXITCODE -eq 0) 'dotnet tool install NuGetCooldown'

    $toolExe = Join-Path $toolDir 'nuget-cooldown'
    $reported = & $toolExe --version 2>&1 | Out-String
    Assert ($reported.Trim() -eq $Version) "tool reports version $Version (got '$($reported.Trim())')"

    # The sample references the freshly published NuGetCooldown.MSBuild, which is itself younger
    # than the default window, so allow-list it (as the MSBuild build steps do via a property);
    # the point of these steps is to exercise the standalone tool, not to re-flag our own package.
    $allowSelf = '--allow', 'NuGetCooldown.MSBuild'

    & $toolExe check (Join-Path $work 'App') @allowSelf | Out-Null
    Assert ($LASTEXITCODE -eq 0) 'tool check passes with defaults'

    & $toolExe check (Join-Path $work 'App') --days 3000 @allowSelf | Out-Null
    Assert ($LASTEXITCODE -eq 1) 'tool check fails with a strict cooldown'

    $json = & $toolExe check (Join-Path $work 'App') @allowSelf --format json | Out-String | ConvertFrom-Json
    Assert ($json.schemaVersion -eq 1 -and $json.summary.total -gt 0) 'tool JSON output is well-formed'
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host 'All end-to-end package tests passed.' -ForegroundColor Green
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
exit 0
