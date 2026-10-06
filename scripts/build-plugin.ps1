param([ValidateSet('auto', 'g++', 'cl')][string]$Compiler = 'auto')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = [IO.Path]::GetFullPath((Join-Path $repo 'build/inprocess'))
if (-not $stage.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must stay inside the repository.'
}
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$hostPack = Get-ChildItem (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Host.win-x64') -Directory |
    Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $hostPack) { throw 'Install the .NET 9 x64 SDK before building.' }
$headers = Join-Path $hostPack.FullName 'runtimes/win-x64/native'
$managed = Join-Path $stage 'managed'
& dotnet publish (Join-Path $repo 'backend/managed/yySync.Managed.csproj') -c Release -r win-x64 --self-contained false --output $managed
if ($LASTEXITCODE -ne 0) { throw 'Managed backend build failed.' }
if ($Compiler -eq 'auto') { $Compiler = if (Get-Command g++ -ErrorAction SilentlyContinue) { 'g++' } else { 'cl' } }
$source = Join-Path $repo 'backend/native/bridge.cpp'
$backend = Join-Path $stage 'backend.dll'
if ($Compiler -eq 'g++') {
    & g++ -std=c++17 -O2 -Wall -Wextra -Werror -shared -static "-I$headers" $source -o $backend
} else {
    & cl /nologo /std:c++17 /EHsc /MT /O2 "/I$headers" /LD $source "/Fe:$backend" "/Fo:$(Join-Path $repo 'build/bridge.obj')" /link "/IMPLIB:$(Join-Path $repo 'build/backend.lib')"
}
if ($LASTEXITCODE -ne 0) { throw 'Native backend build failed.' }
Copy-Item -LiteralPath (Join-Path $headers 'nethost.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'plugin/index.js'), (Join-Path $repo 'plugin/manifest.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $stage

# Framework-dependent component with a private .NET root. Only DLLs are loaded;
# no dotnet.exe or application launcher is included or started.
$runtime = Get-ChildItem (Join-Path $dotnetRoot 'shared/Microsoft.NETCore.App') -Directory |
    Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$fxr = Get-ChildItem (Join-Path $dotnetRoot 'host/fxr') -Directory |
    Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $runtime -or -not $fxr) { throw 'Missing .NET 9 runtime files.' }
$runtimeDestination = Join-Path $stage 'runtime/shared/Microsoft.NETCore.App'
$fxrDestination = Join-Path $stage 'runtime/host/fxr'
New-Item -ItemType Directory -Force $runtimeDestination, $fxrDestination | Out-Null
Copy-Item -LiteralPath $runtime.FullName -Destination $runtimeDestination -Recurse
Remove-Item -LiteralPath (Join-Path $runtimeDestination ($runtime.Name + '/createdump.exe')) -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $fxr.FullName -Destination $fxrDestination -Recurse
foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $dotnetRoot $notice) -Destination (Join-Path $stage 'runtime')
}
$zip = Join-Path $repo 'build/yySyncNCM.zip'
$package = Join-Path $repo 'build/yySyncNCM.plugin'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Copy-Item -LiteralPath $zip -Destination $package -Force
Write-Output $package
