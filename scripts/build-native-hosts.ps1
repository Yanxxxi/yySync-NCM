param([ValidateSet('both', 'x86', 'x64')][string]$Architecture = 'both')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$architectures = if ($Architecture -eq 'both') { @('x86', 'x64') } else { @($Architecture) }
foreach ($arch in $architectures) {
    . (Join-Path $PSScriptRoot 'enter-msvc.ps1') -Architecture $arch
    & $nativeCompiler /nologo /std:c++17 /EHsc /MT /O2 /utf-8 (Join-Path $repo 'tests/native-host.cpp') "/Fe:$(Join-Path $repo "build/native-host-$arch.exe")" "/Fo:$(Join-Path $repo "build/native-host-$arch.obj")"
    if ($LASTEXITCODE -ne 0) { throw "Native $arch test host build failed" }
}
