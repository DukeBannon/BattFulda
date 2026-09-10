[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$extensionRoot = Join-Path $env:USERPROFILE '.vscode\extensions'
$extension = Get-ChildItem -LiteralPath $extensionRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object Name -Like 'bartmanabyss.amiga-debug-*' |
    Sort-Object Name -Descending |
    Select-Object -First 1

if (-not $extension) {
    throw 'The BartmanAbyss Amiga Debug extension was not found.'
}

$toolRoot = Join-Path $extension.FullName 'bin\win32'
$gcc = Join-Path $toolRoot 'opt\bin\m68k-amiga-elf-gcc.exe'
$elf2hunk = Join-Path $toolRoot 'elf2hunk.exe'
$exe2adf = Join-Path $toolRoot 'exe2adf.exe'
$winUae = 'C:\Emulators\WinUAE\winuae64.exe'
$winUaeConfig = 'C:\Emulators\WinUAE\Configurations\A1200 Basic.uae'
$kickstart = 'C:\Emulators\WinUAE\FS-UAE Bios\Amiga 1200.rom'

foreach ($requiredFile in $gcc, $elf2hunk, $exe2adf, $winUae, $winUaeConfig, $kickstart) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required A1200 tool or configuration not found: $requiredFile"
    }
}

$outputDirectory = Join-Path $repositoryRoot 'build\amiga'
$elfFile = Join-Path $outputDirectory 'battalion_fulda_a0.elf'
$exeFile = Join-Path $outputDirectory 'battalion_fulda_a0.exe'
$adfFile = Join-Path $outputDirectory 'battalion_fulda_a0.adf'
$mapFile = Join-Path $outputDirectory 'battalion_fulda_a0.map'
$sources = @(
    (Join-Path $repositoryRoot 'amiga\src\startup.c'),
    (Join-Path $repositoryRoot 'amiga\src\main.c')
)

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

if ($Clean) {
    Get-ChildItem -LiteralPath $outputDirectory -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

$compilerArguments = @(
    '-m68020', '-msoft-float', '-O2', '-g', '-nostdlib', '-ffreestanding',
    '-fno-optimize-sibling-calls', '-fno-tree-loop-distribution',
    '-fomit-frame-pointer', '-ffunction-sections', '-fdata-sections',
    '-Wall', '-Wextra', '-Wno-volatile-register-var', '-Wno-array-bounds',
    "-Wl,--emit-relocs,--gc-sections,-Ttext=0,-Map=$mapFile",
    '-o', $elfFile
) + $sources

& $gcc $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Amiga compilation failed with exit code $LASTEXITCODE."
}

& $elf2hunk $elfFile $exeFile
if ($LASTEXITCODE -ne 0) {
    throw "Amiga executable conversion failed with exit code $LASTEXITCODE."
}

& $exe2adf -i $exeFile -a $adfFile -l 'BATT FULDA A0'
if ($LASTEXITCODE -ne 0) {
    throw "Amiga disk creation failed with exit code $LASTEXITCODE."
}

Write-Host "A1200 build successful: $exeFile"
Write-Host "Bootable test disk: $adfFile"

if ($Run) {
    Write-Host 'Launching the existing A1200 configuration with the A0 test disk.'
    Start-Process -FilePath $winUae -WorkingDirectory (Split-Path $winUae) -ArgumentList @(
        '-f', "`"$winUaeConfig`"",
        '-0', "`"$adfFile`"",
        '-G'
    )
}
