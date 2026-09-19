#requires -Version 5.1
# Build Cet4LearningTool, stage a package (dll + xml + manifest + SHA256SUMS) and hot-install it into the running HistoryVulcan.
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Deploy.ps1            # build + install
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Deploy.ps1 -NoInstall # build + stage only
param([switch]$NoInstall)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$cli = [IO.Path]::GetFullPath((Join-Path $root '..\..\2026-023-HistoryVulcan\z-Publish\host\HistoryVulcan.Cli.exe'))

# Host silently skips a module whose manifest version differs from the assembly version.
$projectVersion = ([xml](Get-Content -LiteralPath (Join-Path $root 'Cet4LearningTool.csproj') -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$manifestVersion = (Get-Content -LiteralPath (Join-Path $root 'module.manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($projectVersion -ne $manifestVersion) { throw "version mismatch: csproj=$projectVersion manifest=$manifestVersion" }

& dotnet build (Join-Path $root 'Cet4LearningTool.csproj') -c Release --nologo -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }

$output = Join-Path $root 'bin\Release\net8.0'
$stage = Join-Path $root 'bin\package\Cet4LearningTool'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in 'Cet4LearningTool.dll', 'Cet4LearningTool.xml', 'module.manifest.json') {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination $stage
}

# SHA256SUMS must be UTF-8 without BOM, or the host rejects the package.
$lines = Get-ChildItem -LiteralPath $stage -File | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS'), [string[]]$lines, (New-Object Text.UTF8Encoding $false))

if ($NoInstall) { Write-Host "staged $manifestVersion : $stage"; return }

& $cli --runtime "vulcan.module.install path=$stage" --approve
if ($LASTEXITCODE -ne 0) { throw "install failed: $LASTEXITCODE" }
