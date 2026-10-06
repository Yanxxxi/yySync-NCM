param([ValidateSet('x86', 'x64')][string]$Architecture)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Install the Visual Studio x86/x64 C++ toolset.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
# Capture the environment privately, without printing it or writing it to disk.
$compilerEnvironment = & $env:ComSpec /d /c "`"$vcvars`" $Architecture >nul && set"
if ($LASTEXITCODE -ne 0) { throw "Cannot configure MSVC for $Architecture" }
foreach ($entry in $compilerEnvironment) {
    if ($entry -match '^([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}
$compilerSearchPath = $compilerEnvironment | Where-Object { $_ -match '^Path=' } | Select-Object -Last 1
if ($compilerSearchPath) { $env:Path = ($compilerSearchPath -split '=', 2)[1] }
$nativeCompiler = Join-Path $env:VCToolsInstallDir "bin/Host$($env:VSCMD_ARG_HOST_ARCH)/$Architecture/cl.exe"
if (-not (Test-Path -LiteralPath $nativeCompiler)) { throw "Missing MSVC compiler: $nativeCompiler" }
Write-Output "MSVC configured for $Architecture"
