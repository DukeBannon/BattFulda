[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot '..\build'
$service = 'https://sgx.geodatenzentrum.de/wfs_dlm250'
$bounds = '554000,5610000,574000,5630000,EPSG:25832'
$layers = [ordered]@{
    roads = '42003_l'
    cultivated = '43001_f'
    woods = '43002_f'
    settlements = '41010_f'
    moor = '43005_f'
    swamp = '43006_f'
    rough = '43007_f'
    water_axis = '44004_l'
    standing_water = '44006_f'
    bridges = '53001_l'
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
foreach ($entry in $layers.GetEnumerator()) {
    $parameters = @{
        SERVICE = 'WFS'
        VERSION = '2.0.0'
        REQUEST = 'GetFeature'
        TYPENAMES = "dlm250:objart_$($entry.Value)"
        SRSNAME = 'EPSG:25832'
        BBOX = $bounds
        OUTPUTFORMAT = 'application/json'
    }
    $query = ($parameters.GetEnumerator() | ForEach-Object {
        '{0}={1}' -f [uri]::EscapeDataString($_.Key), [uri]::EscapeDataString($_.Value)
    }) -join '&'
    $destination = Join-Path $outputDirectory "dlm250_$($entry.Key).geojson"
    & curl.exe --ssl-no-revoke -L "$service`?$query" -o $destination
    if ($LASTEXITCODE -ne 0) {
        throw "DLM250 download failed for $($entry.Key)."
    }
}

Write-Host "Downloaded clipped BKG DLM250 layers to $outputDirectory"
