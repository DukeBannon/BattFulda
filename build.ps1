[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Clean,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$viceExecutable = 'C:\Emulators\VICE\bin\x64sc.exe'
$buildDirectory = Join-Path $repositoryRoot 'build'
$generatedDirectory = Join-Path $repositoryRoot 'generated'
$dataCompiler = Join-Path $repositoryRoot 'tools\compile_data.py'
$cSource = Join-Path $repositoryRoot 'src\main.c'
$assetSource = Join-Path $repositoryRoot 'src\assets.s'
$assetObject = Join-Path $buildDirectory 'assets.o'
$outputFile = Join-Path $buildDirectory 'battalion_fulda_d1.prg'
$mapFile = Join-Path $buildDirectory 'battalion_fulda_d1.map'
$labelFile = Join-Path $buildDirectory 'battalion_fulda_d1.lbl'

foreach ($tool in 'python', 'ca65', 'cl65') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool was not found on PATH."
    }
}

New-Item -ItemType Directory -Force -Path $buildDirectory, $generatedDirectory | Out-Null

if ($Clean) {
    Get-ChildItem -LiteralPath $buildDirectory -File |
        Where-Object Name -ne '.gitkeep' |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $generatedDirectory -File |
        Where-Object Name -ne '.gitkeep' |
        Remove-Item -Force
}

& python $dataCompiler
if ($LASTEXITCODE -ne 0) {
    throw "Data compilation failed with exit code $LASTEXITCODE."
}

if ($Test) {
    & python -m unittest discover -s (Join-Path $repositoryRoot 'tools') -p 'test_*.py'
    if ($LASTEXITCODE -ne 0) {
        throw "Data tests failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    & ca65 $assetSource -o $assetObject
    if ($LASTEXITCODE -ne 0) {
        throw "ca65 failed with exit code $LASTEXITCODE."
    }

    & cl65 -t c64 -Oirs -g -m $mapFile -Ln $labelFile -o $outputFile $cSource $assetObject
    if ($LASTEXITCODE -ne 0) {
        throw "cl65 failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) {
    throw "Compilation completed without producing '$outputFile'."
}

Write-Host "Build successful: $outputFile"

if ($Run) {
    if (-not (Test-Path -LiteralPath $viceExecutable -PathType Leaf)) {
        throw "VICE x64sc was not found at '$viceExecutable'."
    }

    Write-Host "Launching VICE: WASD is ready; 1351 mouse is on port 1; keypad joystick is on port 2."
    Write-Host "Mouse capture starts OFF. Click VICE, then press Alt+M when you want mouse control."
    $viceDirectory = Split-Path -Parent $viceExecutable
    $viceArguments = @(
        '-autostartprgmode', '1',
        '-controlport1device', '3',
        '-controlport2device', '1',
        '-joydev2', '1',
        '+mouse',
        '-autostart', $outputFile
    )
    Start-Process -FilePath $viceExecutable -WorkingDirectory $viceDirectory -ArgumentList $viceArguments
}
