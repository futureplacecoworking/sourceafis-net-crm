# A escolha de preservação/remoção é obrigatória; o padrão é perguntar.
[CmdletBinding(DefaultParameterSetName = 'Ask')]
param(
    [Parameter(ParameterSetName = 'Keep', Mandatory = $true)]
    [switch]$KeepData,
    [Parameter(ParameterSetName = 'Remove', Mandatory = $true)]
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') { throw 'Este desinstalador requer Windows.' }

$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\Futureplace\AgenteFS80H'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'Futureplace\AgenteFS80H\data'
$executable = Join-Path $installDirectory 'AgenteFS80H.exe'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FutureplaceAgenteFS80H'
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Futureplace'
$shortcutPath = Join-Path $shortcutDirectory 'Agente FS80H.lnk'

Write-Host 'Antes de desinstalar, revogue o leitor na Central com o agente conectado e aguarde a confirmação da remoção.'
Write-Host 'A desinstalação local não revoga o dispositivo no servidor.'
if ($PSCmdlet.ParameterSetName -eq 'Ask') {
    Write-Host 'M: manter pareamento e templates cifrados para reinstalar nesta conta Windows.'
    Write-Host 'R: remover definitivamente todos os dados locais do agente.'
    Write-Host 'C: cancelar e não alterar a instalação.'
    $choice = Read-Host 'Escolha M, R ou C (Enter cancela)'
    if ($choice -ieq 'M') {
        $KeepData = $true
    } elseif ($choice -ieq 'R') {
        $RemoveData = $true
    } else {
        Write-Host 'Desinstalação cancelada.'
        return
    }
}
if (-not $KeepData -and -not $RemoveData) { throw 'Escolha explicitamente -KeepData ou -RemoveData.' }

if (Test-Path -LiteralPath $executable -PathType Leaf) {
    $request = Start-Process -FilePath $executable -ArgumentList '--shutdown' -PassThru
    if (-not $request.WaitForExit(5000)) {
        throw 'Não foi possível solicitar o encerramento. Feche o agente pela bandeja e tente novamente.'
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $running = @(Get-Process -Name 'AgenteFS80H' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and [string]::Equals($_.Path, $executable, [StringComparison]::OrdinalIgnoreCase) })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($running.Count -ne 0) { throw 'O agente continua em execução. Feche-o pela bandeja e tente novamente.' }
}

Remove-ItemProperty -Path $startupKey -Name 'FutureplaceAgenteFS80H' -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force }
if ($RemoveData -and (Test-Path -LiteralPath $dataDirectory)) {
    Remove-Item -LiteralPath $dataDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $installDirectory) { Remove-Item -LiteralPath $installDirectory -Recurse -Force }
if ((Test-Path -LiteralPath $shortcutDirectory) -and @(Get-ChildItem -LiteralPath $shortcutDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $shortcutDirectory
}

if ($KeepData) {
    Write-Host 'Aplicativo removido. Os dados cifrados foram preservados para esta conta Windows.'
} else {
    Write-Host 'Aplicativo e dados locais removidos. Será necessário parear e cadastrar as digitais novamente.'
}
Write-Host 'O driver e a DLL do fabricante não foram removidos.'
