param([Parameter(Mandatory = $true)][string]$LegacyDll)
$ErrorActionPreference = 'Stop'
$legacyPath = (Resolve-Path -LiteralPath $LegacyDll).Path
$gameDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$pluginProject = Join-Path $PSScriptRoot '..\..\SaltExtraDebug.csproj'
$checksProject = Join-Path $PSScriptRoot 'Items.Tests.csproj'
$hostProject = Join-Path $PSScriptRoot 'MonoHost\Items.MonoHost.csproj'
foreach ($project in @($pluginProject, $checksProject, $hostProject)) {
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Migration test build failed.' }
}
$hostAssembly = Join-Path $PSScriptRoot 'MonoHost\bin\Release\net9.0\Items.MonoHost.dll'
$checksAssembly = Join-Path $PSScriptRoot 'bin\Release\net48\Items.Tests.exe'
$previousLegacy = $env:SALT_LEGACY_ITEM_DLL
try {
    $env:SALT_LEGACY_ITEM_DLL = $legacyPath
    dotnet $hostAssembly $gameDirectory $checksAssembly
    if ($LASTEXITCODE -ne 0) { throw 'Legacy fixture generation failed.' }
    Remove-Item Env:SALT_LEGACY_ITEM_DLL
    dotnet $hostAssembly $gameDirectory $checksAssembly
    if ($LASTEXITCODE -ne 0) { throw 'Migration checks failed.' }
} finally {
    if ($null -eq $previousLegacy) { Remove-Item Env:SALT_LEGACY_ITEM_DLL -ErrorAction SilentlyContinue }
    else { $env:SALT_LEGACY_ITEM_DLL = $previousLegacy }
}
