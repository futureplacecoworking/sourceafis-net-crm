[CmdletBinding()]
param(
    [string]$BundlePath = (Join-Path $PSScriptRoot '../artifacts/bundle'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/setup')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'AgenteFS80H/AgenteFS80H.csproj') -Raw
$appVersion = [string]$project.Project.PropertyGroup.Version
if ($appVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Versão estável major.minor.patch obrigatória.' }
if (Get-ChildItem -LiteralPath $BundlePath -Recurse -Filter 'ftrScanAPI.dll') { throw 'DLL proprietária não pode acompanhar o instalador.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile((Join-Path $repoRoot 'AgenteFS80H/Assets/Reader.png'))
$bitmap = [System.Drawing.Bitmap]::new(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($image, 0, 0, 64, 64)
    $bitmap.Save((Join-Path $repoRoot 'artifacts/setup-brand.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
} finally { $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose() }
$compiler = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Inno Setup*\ISCC.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup não está disponível no runner Windows.' }
& $compiler.FullName "/DAppVersion=$appVersion" (Join-Path $PSScriptRoot 'CentralBiometrica.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador gráfico.' }
$setup = Join-Path $OutputDirectory 'Central-Biometrica-Setup.exe'
if (-not (Test-Path -LiteralPath $setup)) { throw 'Instalador EXE não foi gerado.' }
if ((Get-Item -LiteralPath $setup).Length -gt 128MB) { throw 'Instalador excede limite do atualizador.' }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Central-Biometrica-Setup.exe" | Set-Content -LiteralPath "$setup.sha256" -Encoding ascii
@{
    version = $appVersion
    installerUrl = "https://github.com/futureplacecoworking/sourceafis-net-crm/releases/download/central-biometrica-v$appVersion/Central-Biometrica-Setup.exe"
    sha256 = $hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'update.json') -Encoding utf8NoBOM

# Transport only: bounded artifacts can be materialized by the delivery tool.
# The user receives the original EXE, recombined and checked against its SHA256.
$partsRoot = Join-Path $repoRoot 'artifacts/setup-parts'
$inputStream = [System.IO.File]::OpenRead($setup)
try {
    $buffer = [byte[]]::new(24MB)
    $index = 1
    while (($count = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $directory = Join-Path $partsRoot ('part-{0:00}' -f $index)
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $part = Join-Path $directory ('part-{0:00}.bin' -f $index)
        $outputStream = [System.IO.File]::Create($part)
        try { $outputStream.Write($buffer, 0, $count) } finally { $outputStream.Dispose() }
        $index++
    }
} finally { $inputStream.Dispose() }
Write-Host "Central Biométrica ${appVersion}: EXE e manifesto gerados, SHA256 $hash"
