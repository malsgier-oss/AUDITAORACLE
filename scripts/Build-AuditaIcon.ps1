# Regenerates Resources/Branding/Audita.ico from Resources/Branding/Audita-AppIcon.png
# using scripts/IconGen (Magick.NET, Lanczos resize, multi-size PNG-in-ICO).
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$png = Join-Path $root "Resources\Branding\Audita-AppIcon.png"
$ico = Join-Path $root "Resources\Branding\Audita.ico"
$proj = Join-Path $root "scripts\IconGen\IconGen.csproj"
if (-not (Test-Path -LiteralPath $png)) { throw "Missing $png" }
dotnet run --project $proj -- $png $ico
Write-Host "OK: $ico"
