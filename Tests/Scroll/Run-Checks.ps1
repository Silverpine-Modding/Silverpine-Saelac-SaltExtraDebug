$ErrorActionPreference = 'Stop'
$gameDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$pluginProject = Join-Path $PSScriptRoot '..\..\SaltExtraDebug.csproj'
$checksProject = Join-Path $PSScriptRoot 'Scroll.Tests.csproj'
$hostProject = Join-Path $PSScriptRoot '..\Items\MonoHost\Items.MonoHost.csproj'
foreach ($project in @($pluginProject, $checksProject, $hostProject)) {
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Scroll test build failed.' }
}
$hostAssembly = Join-Path $PSScriptRoot '..\Items\MonoHost\bin\Release\net9.0\Items.MonoHost.dll'
$checksAssembly = Join-Path $PSScriptRoot 'bin\Release\net48\Scroll.Tests.exe'
dotnet $hostAssembly $gameDirectory $checksAssembly
if ($LASTEXITCODE -ne 0) { throw 'Scroll checks failed.' }
