[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'windows\BattalionFulda.Game\BattalionFulda.Game.csproj'

if ($Clean) {
    dotnet clean $project --configuration Debug
}

dotnet build $project --configuration Debug

if ($Run) {
    dotnet run --project $project --configuration Debug --no-build
}
