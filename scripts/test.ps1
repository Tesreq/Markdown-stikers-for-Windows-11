param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Offline,
    [switch]$NoBuild
)
. "$PSScriptRoot\common.ps1"
if (!$NoBuild) { & "$PSScriptRoot\build.ps1" -Configuration $Configuration -Offline:$Offline }
$runDirectory = Resolve-WorkspacePath ('.local\test-runs\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$coreDirectory = Join-Path $runDirectory 'core'
Invoke-ProjectDotnet -Arguments @('run', '--project', (Join-Path $ProjectRoot 'tests\StickyDesk.Core.Tests'), '-c', $Configuration, '--no-build', '--', $coreDirectory)
$nativeDirectory = Join-Path $runDirectory 'native'
$nativeExecutable = Join-Path $ProjectRoot "tests\StickyDesk.UIHarness\bin\$Configuration\net10.0-windows\StickyDesk.UIHarness.exe"
if (!(Test-Path -LiteralPath $nativeExecutable)) { throw 'Нативные проверки не собраны. Запустите скрипт без -NoBuild.' }
$nativeProcess = Start-Process -FilePath $nativeExecutable -ArgumentList @(('"' + $nativeDirectory + '"'), '--self-test') -WindowStyle Hidden -PassThru -Wait
$report = Join-Path $nativeDirectory 'ui-self-test-results.txt'
if (Test-Path -LiteralPath $report) { Get-Content -Encoding UTF8 -LiteralPath $report }
if ($nativeProcess.ExitCode -ne 0) { throw "Нативные проверки завершились с кодом $($nativeProcess.ExitCode)." }
if (!(Test-Path -LiteralPath $report)) { throw 'Отчёт нативных проверок не создан.' }
Write-Host "Проверки пройдены. Результаты: $runDirectory"
