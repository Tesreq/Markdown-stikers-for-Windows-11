param([string]$SetupPath)
. "$PSScriptRoot\common.ps1"
if (!$SetupPath) {
    $project = Join-Path $ProjectRoot 'src\StickyDesk.App\StickyDesk.App.csproj'
    $version = ([xml](Get-Content -Encoding UTF8 -Raw -LiteralPath $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (!$version) { throw 'Версия приложения не найдена.' }
    $SetupPath = "StickyDesk-Setup-$version-x64.exe"
}
$setup = Resolve-WorkspacePath $SetupPath
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{1F17D3F9-1F43-40CA-94E5-7A9D8C613426}_is1'
if (Test-Path -LiteralPath $registration) { throw 'StickyDesk уже установлен. Тест не будет изменять существующую установку.' }
$runRoot = Resolve-WorkspacePath ('.local\installer-tests\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$install = Resolve-WorkspacePath (Join-Path $runRoot 'app')
$data = Resolve-WorkspacePath (Join-Path $runRoot 'data')
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$results = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw $Name }
    $results.Add('PASS ' + $Name)
}
function Invoke-Setup([string]$LogName) {
    $arguments = @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=""',
        '/LANG=russian', ('/DIR="' + $install + '"'), ('/LOG="' + (Join-Path $runRoot $LogName) + '"'))
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { Stop-Process -Id $process.Id; throw 'Тестовый установщик не завершился за 45 секунд.' }
    return $process.ExitCode
}
$running = $null
$uninstalled = $false
try {
    Check ((Invoke-Setup 'install.log') -eq 0) 'Silent installation succeeds in an isolated project directory'
    $executable = Join-Path $install 'StickyDesk.exe'
    $payload = Join-Path $ProjectRoot '.local\installer-payload\StickyDesk.exe'
    Check ((Test-Path -LiteralPath $executable) -and (Get-FileHash -LiteralPath $executable).Hash -eq (Get-FileHash -LiteralPath $payload).Hash) 'Installed executable matches the published payload'
    Check ((Test-Path -LiteralPath (Join-Path $install 'THIRD_PARTY_NOTICES.txt')) -and (Test-Path -LiteralPath (Join-Path $install 'unins000.exe'))) 'License notices and uninstaller are installed'
    $entry = Get-ItemProperty -LiteralPath $registration
    Check ($entry.InstallLocation.TrimEnd('\') -eq $install) 'Windows uninstall registration points only to the test directory'
    Check (!(Test-Path -LiteralPath (Join-Path $install 'config.json')) -and !(Test-Path -LiteralPath (Join-Path $install 'notes'))) 'Installer does not bundle personal configuration or notes'
    New-Item -ItemType Directory -Path (Join-Path $data 'notes') -Force | Out-Null
    $note = Join-Path $data 'notes\Проверка.md'
    [IO.File]::WriteAllText($note, 'Тестовая заметка установщика', [Text.Encoding]::UTF8)
    $record = @{ version=1; settings=@{ autosaveDelayMs=700 }; notes=@(@{ id='12345678123456781234567812345678'; title='Проверка'; filePath='notes\Проверка.md'; isOwned=$true; x=100; y=100; width=280; height=300; color='#FFF2B3' }) }
    [IO.File]::WriteAllText((Join-Path $data 'config.json'), ($record | ConvertTo-Json -Depth 5), [Text.Encoding]::UTF8)
    $noteHash = (Get-FileHash -LiteralPath $note).Hash
    $running = Start-Process -FilePath $executable -ArgumentList @('--data-dir', ('"' + $data + '"')) -WindowStyle Hidden -PassThru
    $diagnostic = Join-Path $ProjectRoot 'tools\StickyDesk.DesktopDiagnostics\bin\Release\net10.0\StickyDesk.DesktopDiagnostics.exe'
    $attached = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    do {
        Start-Sleep -Milliseconds 250
        $state = @((& $diagnostic $running.Id --verify | Out-String | ConvertFrom-Json))
        $attached = $LASTEXITCODE -eq 0 -and $state.Count -gt 0 -and @($state | Where-Object { !$_.Visible -or !$_.ChildWindow -or !$_.RenderContractSatisfied -or $_.TopMost }).Count -eq 0
    } until ($attached -or [DateTime]::UtcNow -ge $deadline -or $running.HasExited)
    Check $attached 'Installed application starts with the desktop widget rendering contract'
    Check ((Invoke-Setup 'blocked-upgrade.log') -ne 0) 'Upgrade refuses to replace an application that is still running'
    Stop-Process -Id $running.Id
    $running.WaitForExit()
    $running = $null
    Check ((Invoke-Setup 'upgrade.log') -eq 0) 'Repeated installation updates the existing test installation'
    $sentinel = Join-Path $install 'user-note-sentinel.md'
    [IO.File]::WriteAllText($sentinel, 'Keep this file', [Text.Encoding]::UTF8)
    $uninstaller = Join-Path $install 'unins000.exe'
    $uninstallArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $runRoot 'uninstall.log') + '"'))
    $process = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArgs -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { Stop-Process -Id $process.Id; throw 'Тестовое удаление не завершилось за 45 секунд.' }
    Check ($process.ExitCode -eq 0 -and !(Test-Path -LiteralPath $executable)) 'Uninstall removes the installed application'
    $uninstalled = $true
    Check (!(Test-Path -LiteralPath $registration)) 'Uninstall removes its Windows registration'
    Check ((Get-FileHash -LiteralPath $note).Hash -eq $noteHash -and (Test-Path -LiteralPath $sentinel)) 'Uninstall preserves note data and unowned files'
} finally {
    if ($running -and !$running.HasExited) { Stop-Process -Id $running.Id; $running.WaitForExit() }
    if (!$uninstalled -and (Test-Path -LiteralPath (Join-Path $install 'unins000.exe'))) {
        $entry = Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue
        if ($entry -and $entry.InstallLocation.TrimEnd('\') -eq $install) {
            $cleanup = Start-Process -FilePath (Join-Path $install 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru
            if (!$cleanup.WaitForExit(45000)) { Stop-Process -Id $cleanup.Id }
        }
    }
    [IO.File]::WriteAllLines((Join-Path $runRoot 'results.txt'), $results, [Text.Encoding]::UTF8)
    $results | Write-Host
    Write-Host "Результаты проверки установщика: $runRoot"
}
