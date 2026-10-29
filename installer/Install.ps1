# Instala somente para a conta atual. Execute a partir do ZIP extraído.
[CmdletBinding()]
param(
    [switch]$NoStartup,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'O Agente FS80H requer Windows 10/11 de 64 bits.'
}

$appSource = Join-Path $PSScriptRoot 'app'
$programsRoot = Join-Path $env:LOCALAPPDATA 'Programs\Futureplace'
$installDirectory = Join-Path $programsRoot 'AgenteFS80H'
$executable = Join-Path $installDirectory 'AgenteFS80H.exe'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupName = 'FutureplaceAgenteFS80H'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FutureplaceAgenteFS80H'
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Futureplace'
$shortcutPath = Join-Path $shortcutDirectory 'Agente FS80H.lnk'

foreach ($required in @('AgenteFS80H.exe', 'AgenteFS80H.Core.dll', 'SourceAFIS.dll', 'coreclr.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appSource $required) -PathType Leaf)) {
        throw 'Pacote incompleto. Extraia o ZIP completo antes de executar Install.ps1.'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -PathType Leaf)) {
    throw 'O desinstalador não foi encontrado no pacote.'
}
if (Get-ChildItem -LiteralPath $appSource -Recurse -Filter 'ftrScanAPI.dll') {
    throw 'O pacote contém biblioteca proprietária. Use o pacote oficial deste projeto, sem DLL Futronic.'
}

function Stop-InstalledAgent {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { return }
    $request = Start-Process -FilePath $executable -ArgumentList '--shutdown' -PassThru
    if (-not $request.WaitForExit(5000)) {
        throw 'Não foi possível solicitar o encerramento. Feche o agente pela bandeja e tente novamente.'
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $running = @(Get-Process -Name 'AgenteFS80H' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and [string]::Equals($_.Path, $executable, [StringComparison]::OrdinalIgnoreCase) })
        if ($running.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'O agente continua em execução. Feche-o pela bandeja antes de atualizar.'
}

New-Item -ItemType Directory -Path $programsRoot -Force | Out-Null
$stagingDirectory = Join-Path $programsRoot ('.AgenteFS80H.install-' + [Guid]::NewGuid().ToString('N'))
$backupDirectory = Join-Path $programsRoot ('.AgenteFS80H.previous-' + [Guid]::NewGuid().ToString('N'))
$movedPrevious = $false
try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    Get-ChildItem -LiteralPath $appSource -Force | Copy-Item -Destination $stagingDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $stagingDirectory
    $guide = Join-Path $PSScriptRoot 'LEIA-ME.md'
    if (Test-Path -LiteralPath $guide) { Copy-Item -LiteralPath $guide -Destination $stagingDirectory }
    $license = Join-Path $PSScriptRoot 'LICENSE'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination $stagingDirectory }

    Stop-InstalledAgent
    if (Test-Path -LiteralPath $installDirectory) {
        Move-Item -LiteralPath $installDirectory -Destination $backupDirectory
        $movedPrevious = $true
    }
    try {
        Move-Item -LiteralPath $stagingDirectory -Destination $installDirectory
    } catch {
        if ($movedPrevious -and -not (Test-Path -LiteralPath $installDirectory)) {
            Move-Item -LiteralPath $backupDirectory -Destination $installDirectory
            $movedPrevious = $false
        }
        throw
    }

    New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = 'Leitor de digitais Futureplace — autorização pela Central'
    $shortcut.Save()

    if (-not (Test-Path -LiteralPath $startupKey)) { New-Item -Path $startupKey | Out-Null }
    if ($NoStartup) {
        Remove-ItemProperty -Path $startupKey -Name $startupName -ErrorAction SilentlyContinue
    } else {
        New-ItemProperty -Path $startupKey -Name $startupName -PropertyType String -Value ('"' + $executable + '" --background') -Force | Out-Null
    }

    if (-not (Test-Path -LiteralPath $uninstallKey)) { New-Item -Path $uninstallKey | Out-Null }
    $uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installDirectory 'Uninstall.ps1') + '"'
    $version = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = '1.0.0' }
    foreach ($entry in @{
        DisplayName = 'Futureplace — Agente FS80H'
        DisplayVersion = $version
        Publisher = 'Futureplace Coworking'
        InstallLocation = $installDirectory
        DisplayIcon = $executable
        UninstallString = $uninstallCommand
    }.GetEnumerator()) {
        New-ItemProperty -Path $uninstallKey -Name $entry.Key -PropertyType String -Value $entry.Value -Force | Out-Null
    }
    New-ItemProperty -Path $uninstallKey -Name 'NoModify' -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -PropertyType DWord -Value 1 -Force | Out-Null

    Write-Host 'Agente FS80H instalado para a conta atual.'
    Write-Host 'O driver e a DLL Futronic devem ser obtidos separadamente do fabricante.'
    Write-Host 'A atualização preserva os dados locais cifrados. Nenhuma fechadura é acionada nesta versão.'
    if (-not $NoStart) { Start-Process -FilePath $executable -WorkingDirectory $installDirectory | Out-Null }
} finally {
    if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
    if ($movedPrevious -and (Test-Path -LiteralPath $backupDirectory) -and (Test-Path -LiteralPath $installDirectory)) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}
