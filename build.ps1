[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$kickAssemblerJar = 'C:\MiscApps\KickAssembler\KickAss.jar'
$viceExecutable = 'C:\Emulators\VICE\bin\x64sc.exe'
$sourceFile = Join-Path $repositoryRoot 'src\main.asm'
$buildDirectory = Join-Path $repositoryRoot 'build'
$outputName = 'battalion_fulda_d0.prg'
$outputFile = Join-Path $buildDirectory $outputName

if (-not (Test-Path -LiteralPath $kickAssemblerJar -PathType Leaf)) {
    throw "Kick Assembler was not found at '$kickAssemblerJar'."
}

if (-not (Get-Command java -ErrorAction SilentlyContinue)) {
    throw "Java was not found on PATH as 'java'."
}

New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null

# Set explicit output locations so every generated artifact stays in build\.
Push-Location $buildDirectory
try {
    & java -jar $kickAssemblerJar $sourceFile -o $outputName -symbolfile -symbolfiledir $buildDirectory -vicesymbols
    if ($LASTEXITCODE -ne 0) {
        throw "Kick Assembler failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) {
    throw "Assembly completed without producing '$outputFile'."
}

Write-Host "Build successful: $outputFile"

if ($Run) {
    if (-not (Test-Path -LiteralPath $viceExecutable -PathType Leaf)) {
        throw "VICE x64sc was not found at '$viceExecutable'."
    }

    Write-Host "Launching in VICE: $outputFile"
    $viceDirectory = Split-Path -Parent $viceExecutable
    Start-Process -FilePath $viceExecutable -WorkingDirectory $viceDirectory -ArgumentList @('-autostart', $outputFile)
}
