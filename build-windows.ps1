[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Run,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'windows\BattalionFulda.Game\BattalionFulda.Game.csproj'
$movementTests = Join-Path $projectRoot 'windows\BattalionFulda.MovementTests\BattalionFulda.MovementTests.csproj'

if ($Clean) {
    dotnet clean $project --configuration Debug
}

dotnet build $project --configuration Debug

if ($Test) {
    dotnet run --project $movementTests --configuration Debug
}

if ($Run) {
    dotnet run --project $project --configuration Debug --no-build
}
