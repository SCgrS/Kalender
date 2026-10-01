# Kalender'i tek dosya, kendi kendine yeten bir exe olarak yayina hazirlar.
#
#   powershell -ExecutionPolicy Bypass -File yayin.ps1
#
# Cikti: dagitim\Kalender-Kurulum.exe
#
# Ayri bir kurulum programi yok: ayni dosya hem uygulama hem kurucu. Adinda
# "Kurulum" gectigi icin cift tiklandiginda kurulum ekrani aciliyor, kurulan
# kopya ise normal uygulama olarak calisiyor.

$ErrorActionPreference = 'Stop'
$kok = Split-Path -Parent $MyInvocation.MyCommand.Path
$proje = Join-Path $kok "src\Kalender.App\Kalender.App.csproj"
$cikti = Join-Path $kok "dagitim"
$gecici = Join-Path $cikti "_ara"

# .NET 10 SDK'si olan dotnet'i bul: PATH'teki, kullanici klasorundeki
# (%USERPROFILE%\.dotnet; yonetici hakki olmadan kurulan) ya da Program Files'taki.
$adaylar = @()
$yoldaki = Get-Command dotnet -ErrorAction SilentlyContinue
if ($yoldaki) { $adaylar += $yoldaki.Source }
$adaylar += (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")
$adaylar += "C:\Program Files\dotnet\dotnet.exe"
$dotnet = $adaylar | Where-Object { (Test-Path $_) -and ((& $_ --list-sdks) -match '^10\.') } | Select-Object -First 1
if (-not $dotnet) { throw ".NET 10 SDK bulunamadi. https://dotnet.microsoft.com/download adresinden .NET 10 SDK kurun." }
Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray

if (Test-Path $cikti) { Remove-Item $cikti -Recurse -Force }
New-Item -ItemType Directory -Force -Path $gecici | Out-Null

Write-Host "Yayin hazirlaniyor..." -ForegroundColor Cyan

# Sikistirma bilerek kapali: sikistirilmis tek dosyada kutuphaneler acilista
# bellege aciliyor, Kalender ~100 MB fazla RAM tutuyordu (ayrinti csproj'da).
& $dotnet publish $proje `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -o $gecici

if ($LASTEXITCODE -ne 0) { throw "Yayin basarisiz oldu." }

$kaynak = Join-Path $gecici "Kalender.exe"
if (-not (Test-Path $kaynak)) { throw "Cikti dosyasi olusmadi: $kaynak" }

$hedef = Join-Path $cikti "Kalender-Kurulum.exe"
Copy-Item $kaynak $hedef -Force
Remove-Item $gecici -Recurse -Force

$mb = [Math]::Round((Get-Item $hedef).Length / 1MB, 1)
$hash = (Get-FileHash $hedef -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Tamam: $hedef  ($mb MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor DarkGray
