<#
.SYNOPSIS
  Downloads an OpenStreetMap extract (roads, buildings, land use, signals, crossings) for one map
  area from the public Overpass API and saves it as data/osm/<name>.json. Only the bounding box
  is sent. The data is (c) OpenStreetMap contributors, ODbL 1.0: the game shows the credit.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\osm\fetch-osm.ps1            # Tahlia / Al Rawdah
  powershell -ExecutionPolicy Bypass -File E:\MASAR\tools\osm\fetch-osm.ps1 -Name corniche -South 21.58 -West 39.10 -North 21.595 -East 39.115
#>
param(
  [string]$Name = "tahlia-rawdah",
  # Tahlia Street (Prince Mohammed bin Abdulaziz St) through Al Rawdah / Al Andalus: about 1.6 x 1.6 km
  [double]$South = 21.5463, [double]$West = 39.1483, [double]$North = 21.5607, [double]$East = 39.1637,
  [string]$Endpoint = "https://overpass-api.de/api/interpreter"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir = Join-Path $repo "data\osm"
New-Item -ItemType Directory -Force $outDir | Out-Null
$inv = [Globalization.CultureInfo]::InvariantCulture
$bbox = [string]::Format($inv, "{0},{1},{2},{3}", $South, $West, $North, $East)
$query = @"
[out:json][timeout:180];
(
  way["highway"]($bbox);
  way["building"]($bbox);
  relation["building"]($bbox);
  way["building:part"]($bbox);
  way["landuse"]($bbox);
  way["leisure"]($bbox);
  way["natural"]($bbox);
  way["amenity"]($bbox);
  way["barrier"]($bbox);
  node["highway"~"traffic_signals|crossing|stop|give_way|street_lamp|motorway_junction"]($bbox);
  node["natural"="tree"]($bbox);
  node["amenity"~"place_of_worship|fuel"]($bbox);
);
out body;
>;
out skel qt;
"@
$out = Join-Path $outDir "$Name.json"
$tmp = "$out.part"
Write-Host "Downloading OSM data for $Name ($bbox)..."
# The query goes through a file: Windows PowerShell strips the quotes inside native arguments
$qf = "$out.query"
[IO.File]::WriteAllText($qf, $query)
$endpoints = @($Endpoint, "https://overpass.private.coffee/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter") | Select-Object -Unique
$ok = $false; $wait = 5
for ($try = 0; $try -lt 6 -and -not $ok; $try++) {
  $ep = $endpoints[$try % $endpoints.Count]
  $code = & curl.exe -s -A "MASAR-map-tool/0.1" --data-urlencode "data@$qf" $ep -o $tmp -w "%{http_code}"
  if ($code -eq "200" -and (Get-Content $tmp -TotalCount 1 -ErrorAction SilentlyContinue) -match '^\{') { $ok = $true; $Endpoint = $ep; break }
  Write-Host "  $ep answered HTTP $code; retrying in $wait s..."
  Start-Sleep $wait; $wait *= 2
}
Remove-Item $qf -ErrorAction SilentlyContinue
if (-not $ok) { Remove-Item $tmp -ErrorAction SilentlyContinue; throw "All Overpass servers are busy. Try again in a few minutes." }
Move-Item -Force $tmp $out
$meta = [ordered]@{ name = $Name; south = $South; west = $West; north = $North; east = $East; source = $Endpoint; downloaded = (Get-Date).ToUniversalTime().ToString("o"); license = "ODbL 1.0, (c) OpenStreetMap contributors" }
$meta | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $outDir "$Name.meta.json")
Write-Host ("Saved {0} ({1:N1} MB)" -f $out, ((Get-Item $out).Length / 1MB))
