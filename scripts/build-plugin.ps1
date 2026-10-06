param(
    [ValidateSet('auto', 'g++', 'cl')][string]$Compiler = 'auto',
    [ValidateSet('both', 'x86', 'x64')][string]$Architecture = 'both',
    [string]$RuntimeRootX86 = (Join-Path ${env:ProgramFiles(x86)} 'dotnet'),
    [string]$RuntimeRootX64 = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = [IO.Path]::GetFullPath((Join-Path $repo 'build/inprocess'))
if (-not $stage.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must stay inside the repository.'
}
$dotnetSdk = (Get-Command dotnet).Source
$dotnetRoot = Split-Path $dotnetSdk
if (-not $RuntimeRootX64) { $RuntimeRootX64 = $dotnetRoot }
if ($Compiler -eq 'auto') {
    $Compiler = if (Test-Path -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe')) { 'cl' } else { 'g++' }
}
$architectures = if ($Architecture -eq 'both') { @('x86', 'x64') } else { @($Architecture) }
if ($Compiler -eq 'g++' -and $Architecture -eq 'both') { throw 'Use MSVC for the universal package; MinGW supports one selected architecture per build.' }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$source = Join-Path $repo 'backend/native/bridge.cpp'
$exports = Join-Path $repo 'backend/native/bridge.def'
foreach ($arch in $architectures) {
    $payload = Join-Path $stage $arch
    $managed = Join-Path $payload 'managed'
    & $dotnetSdk publish (Join-Path $repo 'backend/managed/yySync.Managed.csproj') -c Release -r "win-$arch" "-p:PlatformTarget=$arch" -p:Platform=AnyCPU --self-contained false --output $managed
    if ($LASTEXITCODE -ne 0) { throw "Managed $arch backend build failed." }
    $hostPackRoot = Join-Path $dotnetRoot "packs/Microsoft.NETCore.App.Host.win-$arch"
    $hostPack = Get-ChildItem -LiteralPath $hostPackRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if ($hostPack) { $hostPackPath = $hostPack.FullName }
    else {
        $nugetDirectory = ((& $dotnetSdk nuget locals global-packages --list) -split ': ', 2)[1].Trim()
        $hostPackPath = Join-Path $nugetDirectory "microsoft.netcore.app.host.win-$arch/9.0.0"
    }
    $headers = Join-Path $hostPackPath "runtimes/win-$arch/native"
    if (-not (Test-Path -LiteralPath (Join-Path $headers 'nethost.dll'))) { throw "Missing $arch .NET host pack." }
    $dllName = if ($arch -eq 'x86') { 'backend.dll' } else { 'backend.dll.x64.dll' }
    $backend = Join-Path $stage $dllName
    if ($Compiler -eq 'cl') {
        . (Join-Path $PSScriptRoot 'enter-msvc.ps1') -Architecture $arch
        & $nativeCompiler /nologo /std:c++17 /EHsc /MT /O2 /utf-8 "/I$headers" /LD $source "/Fe:$backend" "/Fo:$(Join-Path $repo "build/bridge-$arch.obj")" /link "/DEF:$exports" "/IMPLIB:$(Join-Path $repo "build/backend-$arch.lib")"
    } else {
        $target = & g++ -dumpmachine
        if (($arch -eq 'x86' -and $target -notmatch '^i.86') -or ($arch -eq 'x64' -and $target -notmatch '^x86_64')) {
            throw "MinGW target $target does not match requested architecture $arch."
        }
        & g++ -std=c++17 -O2 -Wall -Wextra -Werror -shared -static "-I$headers" $source $exports -o $backend
    }
    if ($LASTEXITCODE -ne 0) { throw "Native $arch backend build failed." }
    Copy-Item -LiteralPath (Join-Path $headers 'nethost.dll') -Destination $payload
    $runtimeRoot = if ($arch -eq 'x86') { $RuntimeRootX86 } else { $RuntimeRootX64 }
    $runtime = Get-ChildItem (Join-Path $runtimeRoot 'shared/Microsoft.NETCore.App') -Directory |
        Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $fxr = Get-ChildItem (Join-Path $runtimeRoot 'host/fxr') -Directory |
        Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $runtime -or -not $fxr) { throw "Install .NET 9 $arch runtime before building, or set RuntimeRoot$arch." }
    $runtimeDestination = Join-Path $payload 'runtime/shared/Microsoft.NETCore.App'
    $fxrDestination = Join-Path $payload 'runtime/host/fxr'
    New-Item -ItemType Directory -Force $runtimeDestination, $fxrDestination | Out-Null
    Copy-Item -LiteralPath $runtime.FullName -Destination $runtimeDestination -Recurse
    Remove-Item -LiteralPath (Join-Path $runtimeDestination ($runtime.Name + '/createdump.exe')) -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $fxr.FullName -Destination $fxrDestination -Recurse
    foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        Copy-Item -LiteralPath (Join-Path $runtimeRoot $notice) -Destination (Join-Path $payload 'runtime')
    }
}
Copy-Item -LiteralPath (Join-Path $repo 'plugin/index.js'), (Join-Path $repo 'plugin/manifest.json'), (Join-Path $repo 'LICENSE'), (Join-Path $repo 'NOTICE.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination $stage -Recurse
$zip = Join-Path $repo 'build/yySyncNCM.zip'
$package = Join-Path $repo 'build/yySyncNCM.plugin'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Copy-Item -LiteralPath $zip -Destination $package -Force
Write-Output $package
