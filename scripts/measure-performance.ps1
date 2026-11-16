param([int]$Seconds = 10)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'artifacts\StickyDesk-win-x64\StickyDesk.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Сначала выполните scripts/publish.ps1.' }
$data = Join-Path $projectRoot ('.local\benchmarks\performance-' + [Guid]::NewGuid().ToString('N'))
$notesDirectory = Join-Path $data 'notes'
New-Item -ItemType Directory -Path $notesDirectory -Force | Out-Null
$notes = @()
for ($index = 0; $index -lt 10; $index++) {
    $id = [Guid]::NewGuid().ToString('N')
    $name = "performance-$index.md"
    $body = "# Заметка $($index + 1)`n`n- [ ] Задача на сегодня`n- [x] Проверка кириллицы`n`n**Markdown**, обычный текст и числа 0123456789.`n"
    [IO.File]::WriteAllText((Join-Path $notesDirectory $name), $body, [Text.UTF8Encoding]::new($false))
    $notes += @{ id = $id; title = "Проверка $($index + 1)"; filePath = "notes\$name"; isOwned = $true;
        x = 50 + ($index % 5) * 245; y = 50 + [Math]::Floor($index / 5) * 235; width = 230; height = 220; color = '#FFF2B3' }
}
$config = @{ version = 1; settings = @{ autosaveDelayMs = 700 }; notes = $notes } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $data 'config.json'), $config, [Text.UTF8Encoding]::new($false))
$watch = [Diagnostics.Stopwatch]::StartNew()
$application = Start-Process -FilePath $executable -ArgumentList @('--data-dir', ('"' + $data + '"')) -WindowStyle Normal -PassThru
try {
    $log = Join-Path $data 'logs\errors.log'
    $attached = 0
    while ($watch.Elapsed.TotalSeconds -lt 20) {
        if ($application.HasExited) { throw 'Приложение завершилось до начала измерения.' }
        if (Test-Path -LiteralPath $log) {
            $attached = @(Get-Content -LiteralPath $log | Select-String 'Attached note .*visible=True').Count
            if ($attached -ge 10) { break }
        }
        Start-Sleep -Milliseconds 100
    }
    $startup = $watch.Elapsed.TotalMilliseconds
    if ($attached -lt 10) { throw "Прикреплено только $attached из 10 стикеров. Лог: $log" }
    $diagnostic = Join-Path $projectRoot 'tools\StickyDesk.DesktopDiagnostics\bin\Release\net10.0\StickyDesk.DesktopDiagnostics.exe'
    $renderContractVerified = $false
    if (Test-Path -LiteralPath $diagnostic) {
        $verificationOutput = & $diagnostic $application.Id --verify
        if ($LASTEXITCODE -ne 0) { throw 'Не пройдена проверка композиции стикеров.' }
        $checks = ($verificationOutput | Out-String) | ConvertFrom-Json
        if ($checks.Count -ne 10 -or @($checks | Where-Object { !$_.RenderContractSatisfied -or $_.TopMost }).Count -gt 0) { throw 'Неверное состояние тестовых стикеров.' }
        $renderContractVerified = $true
        [IO.File]::WriteAllText((Join-Path $data 'render-verification.json'), ($verificationOutput | Out-String))
    }
    Start-Sleep -Seconds 3
    $application.Refresh()
    $cpuStart = $application.TotalProcessorTime.TotalMilliseconds
    $measurement = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $Seconds
    $application.Refresh()
    $elapsed = $measurement.Elapsed.TotalMilliseconds
    $result = [ordered]@{
        measuredAt = [DateTimeOffset]::Now.ToString('o')
        osVersion = [Environment]::OSVersion.Version.ToString()
        logicalProcessors = [Environment]::ProcessorCount
        notes = $attached
        version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
        renderContractVerified = $renderContractVerified
        startupUntilTenVisibleMs = [Math]::Round($startup, 0)
        workingSetMiB = [Math]::Round($application.WorkingSet64 / 1MB, 2)
        privateBytesMiB = [Math]::Round($application.PrivateMemorySize64 / 1MB, 2)
        idleCpuPercentOfMachine = [Math]::Round(($application.TotalProcessorTime.TotalMilliseconds - $cpuStart) / $elapsed / [Environment]::ProcessorCount * 100, 4)
        idleSampleSeconds = [Math]::Round($elapsed / 1000, 2)
        dataDirectory = $data
    }
    $json = $result | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $data 'performance.json'), $json)
    $json
} finally {
    if (!$application.HasExited) { Stop-Process -Id $application.Id }
}
