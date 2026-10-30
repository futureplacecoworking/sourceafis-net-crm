# Executes only on the disposable GitHub Windows runner. No real scanner, network pairing or biometrics.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SetupPath,
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [string]$OutputDirectory = 'artifacts/setup-tests'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$SetupPath = (Resolve-Path -LiteralPath $SetupPath).Path
$BundlePath = (Resolve-Path -LiteralPath $BundlePath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installed = Join-Path $env:LOCALAPPDATA 'Programs\Futureplace\AgenteFS80H'
$executable = Join-Path $installed 'AgenteFS80H.exe'
$data = Join-Path $env:LOCALAPPDATA 'Futureplace\AgenteFS80H\data'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'FutureplaceAgenteFS80H'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FutureplaceAgenteFS80H'
$uninstallKey = $legacyKey + '_is1'
$group = Join-Path ([Environment]::GetFolderPath('Programs')) 'Futureplace'
$shortcut = Join-Path $group 'Central Biométrica.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Central Biométrica.lnk'
$legacyShortcut = Join-Path $group 'Agente FS80H.lnk'
if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $uninstallKey)) {
    throw 'The installer smoke test requires a disposable clean Windows profile.'
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Find-Button($Window, [string]$Name) {
    $buttons = $Window.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
    foreach ($button in $buttons) {
        if (($button.Current.Name -replace '&', '') -eq $Name -and -not $button.Current.IsOffscreen -and $button.Current.IsEnabled) { return $button }
    }
    return $null
}

function Find-Wizard([Diagnostics.Process]$Loader, [string]$ButtonName = '') {
    # Inno's SetupLdr owns a hidden STATIC window; the real wizard belongs to its setup.tmp child.
    # Match only top-level windows from the loader's process tree, never an unrelated Setup window.
    $relatedProcessIds = New-Object 'System.Collections.Generic.HashSet[int]'
    [void]$relatedProcessIds.Add($Loader.Id)
    $processes = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId)
    do {
        $added = $false
        foreach ($processRecord in $processes) {
            if ($relatedProcessIds.Contains([int]$processRecord.ParentProcessId) -and $relatedProcessIds.Add([int]$processRecord.ProcessId)) { $added = $true }
        }
    } while ($added)
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition)
    foreach ($candidate in $windows) {
        if ($relatedProcessIds.Contains($candidate.Current.ProcessId) -and $candidate.Current.Name.IndexOf('Central Biométrica', [StringComparison]::OrdinalIgnoreCase) -ge 0 -and -not $candidate.Current.IsOffscreen) {
            if (-not $ButtonName -or (Find-Button $candidate $ButtonName)) { return $candidate }
        }
    }
    return $null
}

function Get-ApplicationWindows([Diagnostics.Process]$Application) {
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $Application.Id)
    $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $condition)
    foreach ($candidate in $windows) { $candidate }
}

function Find-ApplicationWindow([Diagnostics.Process]$Application) {
    foreach ($candidate in @(Get-ApplicationWindows $Application)) {
        # A generic Program/ThreadException MessageBox has the same product title.
        # Require the real, enabled WinForms operator page instead of accepting any HWND.
        if ($candidate.Current.ClassName.StartsWith('WindowsForms10.Window', [StringComparison]::Ordinal) -and
            -not $candidate.Current.IsOffscreen -and $candidate.Current.IsEnabled -and
            (Find-Button $candidate 'Leitura') -and (Find-Button $candidate 'Configurações')) { return $candidate }
    }
    return $null
}

function Save-ApplicationEvidence([Diagnostics.Process]$Application, [string]$Label) {
    $Application.Refresh()
    $fresh = Get-Process -Id $Application.Id -ErrorAction SilentlyContinue
    Write-Host "Application evidence ${Label}: PID=$($Application.Id) exited=$($Application.HasExited) cachedMain=$($Application.MainWindowHandle) freshMain=$(if($fresh){$fresh.MainWindowHandle}else{'exited'})"
    $records = @()
    $index = 0
    foreach ($candidate in @(Get-ApplicationWindows $Application)) {
        $index++
        $texts = $candidate.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
        $visibleText = @($texts | ForEach-Object { $_.Current.Name })
        $record = @{ title=$candidate.Current.Name; class=$candidate.Current.ClassName; enabled=$candidate.Current.IsEnabled; offscreen=$candidate.Current.IsOffscreen; processId=$candidate.Current.ProcessId; handle=$candidate.Current.NativeWindowHandle; text=$visibleText }
        $records += $record
        Write-Host ($record | ConvertTo-Json -Depth 4 -Compress)
        if (-not $candidate.Current.IsOffscreen -and $candidate.Current.BoundingRectangle.Width -gt 0 -and $candidate.Current.BoundingRectangle.Height -gt 0) {
            Save-Wizard $candidate ("application-$Label-$index.png")
        }
    }
    $records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory ("application-$Label.json"))
}

function Save-Wizard($Window, [string]$Name) {
    $rect = $Window.Current.BoundingRectangle
    $bitmap = New-Object Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $deviceContext = $graphics.GetHdc()
        try { $printed = [CentralBiometricaSetupTest.Native]::PrintWindow([IntPtr]$Window.Current.NativeWindowHandle, $deviceContext, 2) }
        finally { $graphics.ReleaseHdc($deviceContext) }
        if (-not $printed) { $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size) }
        $bitmap.Save((Join-Path $OutputDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Invoke-GraphicalInstall {
    $wizard = Start-Process -FilePath $SetupPath -ArgumentList @('/SP-', '/NORESTART', ('/LOG="' + (Join-Path $OutputDirectory 'graphical-install.log') + '"')) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $installButton = $null
    $window = $null
    do {
        Start-Sleep -Milliseconds 100
        $wizard.Refresh()
        $window = Find-Wizard $wizard 'Instalar'
        if ($window) { $installButton = Find-Button $window 'Instalar' }
    } while (-not $installButton -and -not $wizard.HasExited -and [DateTime]::UtcNow -lt $deadline)
    Assert-True ($null -ne $installButton) 'The graphical installer did not offer Instalar directly.'
    Save-Wizard $window 'central-biometrica-instalar.png'
    Assert-True ([CentralBiometricaSetupTest.Native]::PostMessage([IntPtr]$installButton.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)) 'Could not click Instalar.'
    $finishButton = $null
    $capturedProgress = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    do {
        Start-Sleep -Milliseconds 60
        $wizard.Refresh()
        if (-not $wizard.HasExited) {
            $progress = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ProgressBar)))
            if ($progress -and -not $progress.Current.IsOffscreen -and -not $capturedProgress) { Save-Wizard $window 'central-biometrica-progresso.png'; $capturedProgress = $true }
            $finishButton = Find-Button $window 'Concluir'
        }
    } while (-not $finishButton -and -not $wizard.HasExited -and [DateTime]::UtcNow -lt $deadline)
    Assert-True $capturedProgress 'The graphical installation did not expose its progress bar.'
    Assert-True ($null -ne $finishButton) 'The graphical installation did not reach Concluir.'
    Save-Wizard $window 'central-biometrica-concluir.png'
    Assert-True ([CentralBiometricaSetupTest.Native]::PostMessage([IntPtr]$finishButton.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)) 'Could not click Concluir.'
    Assert-True ($wizard.WaitForExit(15000)) 'Concluir did not close Setup.'
    Assert-True ($wizard.ExitCode -eq 0) 'Graphical installation failed.'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $application = $null
    $operatorWindow = $null
    do {
        Start-Sleep -Milliseconds 100
        $application = Get-Process -Name 'AgenteFS80H' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -eq $executable } | Select-Object -First 1
        if ($application) { $application.Refresh(); $operatorWindow = Find-ApplicationWindow $application }
    } while (-not $operatorWindow -and [DateTime]::UtcNow -lt $deadline)
    if ($application) { Save-ApplicationEvidence $application 'after-concluir' }
    Assert-True ($null -ne $operatorWindow) 'Concluir did not open the enabled WinForms Leitura/Configurações page; inspect application-after-concluir evidence.'
    Assert-True ($application.CloseMainWindow()) 'The installed main window could not be closed to the tray.'
    Start-Sleep -Milliseconds 350
    $application.Refresh()
    Assert-True (-not $application.HasExited) 'Closing the installed window stopped the reader instead of keeping it in the tray.'
    return $application
}

function Invoke-Setup([string]$Label) {
    $log = Join-Path $OutputDirectory ($Label + '.log')
    $process = Start-Process -FilePath $SetupPath -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $log + '"')) -PassThru
    Assert-True ($process.WaitForExit(90000)) "Setup did not finish: $Label"
    Assert-True ($process.ExitCode -eq 0) "Setup failed with exit code $($process.ExitCode): $Label"
}

function Assert-Installed([bool]$ExpectStartup = $true) {
    Assert-True (Test-Path -LiteralPath $executable) 'The EXE installer did not install the application.'
    $entry = Get-ItemProperty -LiteralPath $uninstallKey
    Assert-True ($entry.DisplayName -eq 'Central Biométrica') 'Windows application registration has the wrong product name.'
    Assert-True ($entry.DisplayVersion -eq $ExpectedVersion) 'Windows application registration has the wrong version.'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    Assert-True ($version.ProductName -eq 'Central Biométrica') 'Installed executable product metadata does not identify Central Biométrica.'
    Assert-True (($version.ProductVersion -split '\+')[0] -eq $ExpectedVersion) 'Installed executable version differs from Setup.'
    if ($ExpectStartup) {
        $startup = (Get-ItemProperty -LiteralPath $runKey -Name $runName).$runName
        Assert-True ($startup -eq ('"' + $executable + '" --background')) 'Windows background startup is missing or points at the wrong binary.'
    } else {
        Assert-True (-not (Get-ItemProperty -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue)) 'Upgrade silently re-enabled startup after the user disabled it.'
    }
    Assert-True (Test-Path -LiteralPath $shortcut) 'Start menu shortcut is missing.'
    Assert-True (Test-Path -LiteralPath $desktopShortcut) 'Desktop shortcut is missing.'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    Assert-True ($link.TargetPath -eq $executable) 'Start menu shortcut points at the wrong executable.'
    Assert-True (-not (Test-Path -LiteralPath $legacyKey)) 'Upgrade left a duplicate legacy application registration.'
    Assert-True (-not (Test-Path -LiteralPath $legacyShortcut)) 'Upgrade left a duplicate legacy Start menu shortcut.'
}

function Assert-Preserved {
    foreach ($file in $script:originalHashes.Keys) {
        Assert-True (Test-Path -LiteralPath $file) "Setup removed preserved profile/library data: $file"
        Assert-True ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $script:originalHashes[$file]) 'Setup modified synthetic protected state, templates or the selected external library.'
    }
}

function Start-Background {
    $process = Start-Process -FilePath $executable -ArgumentList '--background' -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 150
        $process.Refresh()
    } while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline -and $process.TotalProcessorTime.TotalMilliseconds -lt 40)
    Assert-True (-not $process.HasExited) 'Installed application did not remain active in the background.'
    Assert-True ($process.MainWindowHandle -eq [IntPtr]::Zero) 'Background startup displayed an unsolicited main window.'
    return $process
}

function Invoke-Uninstall([string]$Label) {
    $uninstaller = Join-Path $installed 'unins000.exe'
    Assert-True (Test-Path -LiteralPath $uninstaller) 'The native uninstaller is missing.'
    $log = Join-Path $OutputDirectory ($Label + '.log')
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $log + '"')) -PassThru
    Assert-True ($process.WaitForExit(60000)) 'Native uninstall did not finish.'
    Assert-True ($process.ExitCode -eq 0) "Native uninstall failed: $($process.ExitCode)"
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path -LiteralPath $installed) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert-True (-not (Test-Path -LiteralPath $installed)) 'Native uninstall did not remove installed application files.'
    Assert-True (-not (Test-Path -LiteralPath $uninstallKey)) 'Native uninstall left its application registration.'
    Assert-True (-not (Test-Path -LiteralPath $shortcut)) 'Native uninstall left its Start menu shortcut.'
    Assert-True (-not (Test-Path -LiteralPath $desktopShortcut)) 'Native uninstall left its Desktop shortcut.'
    Assert-True (-not (Get-ItemProperty -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue)) 'Native uninstall left Windows startup enabled.'
    Assert-Preserved
}

# Show the real branded wizard once; cancel before installation and preserve its native screenshot.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace CentralBiometricaSetupTest {
    public static class Native {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
    }
}
'@
$preview = Start-Process -FilePath $SetupPath -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(30)
$readyWindow = $null
do {
    Start-Sleep -Milliseconds 150
    $preview.Refresh()
    $readyWindow = Find-Wizard $preview 'Instalar'
} while (-not $readyWindow -and -not $preview.HasExited -and [DateTime]::UtcNow -lt $deadline)
Assert-True ($null -ne $readyWindow) 'The graphical Setup did not show its direct Instalar action.'
Save-Wizard $readyWindow 'central-biometrica-setup.png'
Assert-True ([CentralBiometricaSetupTest.Native]::PostMessage([IntPtr]$readyWindow.Current.NativeWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)) 'Could not cancel the unstarted graphical installer.'
Assert-True ($preview.WaitForExit(15000)) 'Cancelling the unstarted installer did not close it.'
Assert-True (-not (Test-Path -LiteralPath $installed)) 'Cancelling before Instalar created an application installation.'
Assert-True (-not (Test-Path -LiteralPath $uninstallKey)) 'Cancelling before Instalar registered the application.'

# Seed actual DPAPI CurrentUser files with exclusively synthetic content and an external DLL placeholder.
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
New-Item -ItemType Directory -Path (Join-Path $data 'templates') -Force | Out-Null
$externalLibrary = Join-Path $OutputDirectory 'synthetic-library\ftrScanAPI.dll'
New-Item -ItemType Directory -Path (Split-Path $externalLibrary) -Force | Out-Null
'Synthetic placeholder only; no vendor code, no USB loading.' | Set-Content -LiteralPath $externalLibrary
$state = @{ configuration = @{ centralUrl = 'https://central.example'; projectUrl = ''; publicApiKey = ''; scannerDllPath = $externalLibrary; deviceId = '00000000-0000-0000-0000-000000000000'; deviceToken = '' }; pendingRemovals = @(); deviceRevoked = $false; totalRemovalPending = $false }
$template = @{ cadastroId = '20000000-0000-0000-0000-000000000002'; comandoId = '30000000-0000-0000-0000-000000000003'; template = 'BwgJ' }
$entropy = [Text.Encoding]::UTF8.GetBytes('Futureplace.AgenteFS80H.protocolo1')
$stateFile = Join-Path $data 'state.dpapi'
$templateFile = Join-Path $data 'templates\20000000000000000000000000000002.dpapi'
foreach ($fixture in @(@{Path=$stateFile; Value=$state}, @{Path=$templateFile; Value=$template})) {
    $plain = [Text.Encoding]::UTF8.GetBytes(($fixture.Value | ConvertTo-Json -Compress -Depth 8))
    [IO.File]::WriteAllBytes($fixture.Path, [Security.Cryptography.ProtectedData]::Protect($plain, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser))
    [Array]::Clear($plain, 0, $plain.Length)
}
$script:originalHashes = @{}
foreach ($file in @($stateFile, $templateFile, $externalLibrary)) { $script:originalHashes[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }

$agent = Invoke-GraphicalInstall
Assert-Installed
Assert-Preserved
$second = Start-Process -FilePath $executable -ArgumentList '--background' -PassThru
Assert-True ($second.WaitForExit(15000)) 'A second launch did not forward to the existing application instance.'
$agent.Refresh()
Assert-True (-not $agent.HasExited) 'A second launch stopped the original background application.'
$deadline = [DateTime]::UtcNow.AddSeconds(10)
$reopenedWindow = $null
do {
    Start-Sleep -Milliseconds 100
    $agent.Refresh()
    $reopenedWindow = Find-ApplicationWindow $agent
} while (-not $reopenedWindow -and [DateTime]::UtcNow -lt $deadline)
Save-ApplicationEvidence $agent 'after-reopen'
Assert-True ($null -ne $reopenedWindow) 'Second launch did not restore the enabled operator window; inspect application-after-reopen evidence.'
Assert-True ($agent.CloseMainWindow()) 'The reopened application window could not be closed to the tray.'
Start-Sleep -Milliseconds 350
$agent.Refresh()
Assert-True (-not $agent.HasExited) 'Closing the main window stopped the background reader.'

# An update must preserve the preference if the receptionist disabled Windows startup.
Remove-ItemProperty -LiteralPath $runKey -Name $runName
Invoke-Setup 'upgrade-running'
Assert-True ($agent.WaitForExit(5000)) 'Upgrade did not gracefully stop the previous application instance.'
Assert-Installed $false
Assert-Preserved
$agent = Start-Background
Invoke-Uninstall 'uninstall-running'
Assert-True ($agent.WaitForExit(5000)) 'Native uninstall did not gracefully stop the background application.'

# Migrate an actual legacy PowerShell installation without executing its destructive uninstall.
& (Join-Path $BundlePath 'Install.ps1') -NoStart -NoStartup
Assert-True (Test-Path -LiteralPath $legacyKey) 'Legacy migration fixture did not register the old installation.'
Invoke-Setup 'migrate-powershell'
Assert-Installed $false
Assert-Preserved
# Simulate the same startup value written by StartupRegistration.SetEnabled(true) after migration.
New-ItemProperty -LiteralPath $runKey -Name $runName -PropertyType String -Value ('"' + $executable + '" --background') -Force | Out-Null
Assert-Installed
Invoke-Uninstall 'uninstall-migrated'
Write-Host 'Central Biométrica Setup: GUI cancellation, native install, upgrade, legacy migration, background lifecycle, version and data-preserving uninstall passed.'
