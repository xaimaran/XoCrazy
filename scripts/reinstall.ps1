<#
    Dev loop helper.

    VSIXInstaller keys on Identity Id + Version, so reinstalling the same version is refused
    outright. Bumping the version every iteration is churn; uninstalling first is not. This
    closes VS, removes the installed copy, rebuilds, and installs the fresh one.

    Usage:  pwsh -File scripts\reinstall.ps1 [-Configuration Release] [-SkipBuild]
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipBuild,
    [switch] $KeepVsOpen
)

$ErrorActionPreference = 'Stop'

$ExtensionId = 'ThemeForge.7d3f1a20-9c44-4e7b-9f1e-2b6a8c5d3e01'
$RepoRoot    = Split-Path -Parent $PSScriptRoot
# The project was renamed ThemeForge -> XoCrazy; the Identity Id above deliberately was not.
# Changing it would make the next install a second extension beside the old one.
$Project     = Join-Path $RepoRoot 'src\XoCrazy\XoCrazy.csproj'
$VsixPath    = Join-Path $RepoRoot "src\XoCrazy\bin\$Configuration\XoCrazy.vsix"

# Locate the VS install rather than hardcoding a path — this box has it on G:.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Is Visual Studio installed?" }

$installPath = & $vswhere -latest -prerelease -products * -property installationPath
if (-not $installPath) { throw "No Visual Studio installation found." }

$devenv   = Join-Path $installPath 'Common7\IDE\devenv.exe'
$vsixExe  = Join-Path $installPath 'Common7\IDE\VSIXInstaller.exe'
$msbuild  = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'

if (-not $KeepVsOpen) {
    # The installer cannot replace files while devenv holds them open.
    Get-Process devenv -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Closing devenv (PID $($_.Id))..." -ForegroundColor DarkGray
        $_.CloseMainWindow() | Out-Null
    }
    Start-Sleep -Seconds 2
    Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force
}

Write-Host "Uninstalling $ExtensionId ..." -ForegroundColor Cyan
# Exit code 1002 means "not installed", which is a fine starting state.
$uninstall = Start-Process -FilePath $vsixExe -ArgumentList "/q", "/u:$ExtensionId" -Wait -PassThru
if ($uninstall.ExitCode -notin @(0, 1002)) {
    Write-Warning "Uninstall returned $($uninstall.ExitCode); continuing anyway."
}

if (-not $SkipBuild) {
    Write-Host "Building $Configuration ..." -ForegroundColor Cyan
    & $msbuild $Project -t:rebuild -v:m -nologo -p:Configuration=$Configuration -p:DeployExtension=false
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

if (-not (Test-Path $VsixPath)) { throw "VSIX not found at $VsixPath" }

Write-Host "Installing $VsixPath ..." -ForegroundColor Cyan
$install = Start-Process -FilePath $vsixExe -ArgumentList "/q", "`"$VsixPath`"" -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Install returned $($install.ExitCode)." }

Write-Host "Done. Start Visual Studio, open a code file, then right-click > Open XoCrazy." -ForegroundColor Green
