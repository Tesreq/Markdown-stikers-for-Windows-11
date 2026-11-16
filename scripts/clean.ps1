[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$TestData, [switch]$AllGenerated)
. "$PSScriptRoot\common.ps1"
$targets = @()
foreach ($sourceRoot in @('src', 'tests', 'tools')) {
    $directory = Resolve-WorkspacePath $sourceRoot
    if (!(Test-Path -LiteralPath $directory)) { continue }
    foreach ($project in Get-ChildItem -LiteralPath $directory -Filter '*.csproj' -File -Recurse) {
        $targets += Join-Path $project.DirectoryName 'bin'
        $targets += Join-Path $project.DirectoryName 'obj'
    }
}
if ($TestData) { $targets += Resolve-WorkspacePath '.local\test-runs' }
$backups = Resolve-WorkspacePath '.local\backups'
$projectFile = Join-Path $ProjectRoot 'src\StickyDesk.App\StickyDesk.App.csproj'
$version = ([xml](Get-Content -Encoding UTF8 -Raw -LiteralPath $projectFile)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$keepSetup = Resolve-WorkspacePath "StickyDesk-Setup-$version-x64.exe"
if ($AllGenerated) {
    if (!(Test-Path -LiteralPath $keepSetup)) { throw 'Сначала соберите актуальный установщик. Очистка не должна удалить единственный готовый дистрибутив.' }
    $local = Resolve-WorkspacePath '.local'
    if (Test-Path -LiteralPath $local) {
        $targets += Get-ChildItem -LiteralPath $local -Force | Where-Object { $_.Name -ne 'backups' } | Select-Object -ExpandProperty FullName
    }
    $targets += Resolve-WorkspacePath 'artifacts'
    $targets += Get-ChildItem -LiteralPath $ProjectRoot -File -Filter 'StickyDesk-Setup-*.exe' | Where-Object { $_.FullName -ne $keepSetup } | Select-Object -ExpandProperty FullName
    $targets += Get-ChildItem -LiteralPath $ProjectRoot -File -Filter 'StickyDesk-Setup-*.exe.sha256' | Select-Object -ExpandProperty FullName
    foreach ($relative in @('.vs', 'TestResults', '.test-data', 'docs\reports')) { $targets += Resolve-WorkspacePath $relative }
}
$plan = @()
foreach ($target in $targets | Sort-Object -Unique) {
    $resolved = Resolve-WorkspacePath $target
    if (!(Test-Path -LiteralPath $resolved)) { continue }
    $item = Get-Item -Force -LiteralPath $resolved
    foreach ($protected in @($backups, $keepSetup)) {
        if ($resolved -eq $protected -or $resolved.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $protected.StartsWith($resolved + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Защищённый путь попал в очистку: $resolved"
        }
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        @(Get-ChildItem -LiteralPath $resolved -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
        throw "Очистка ссылок и junction не поддерживается: $resolved"
    }
    $files = if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $resolved -Force -Recurse -File) } else { @($item) }
    $plan += [pscustomobject]@{ Path=$resolved; Directory=$item.PSIsContainer; Bytes=[long]($files | Measure-Object -Property Length -Sum).Sum; Files=$files.Count }
}
# Validate the complete deletion plan before changing anything.
foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    if (!$process.Path) { continue }
    foreach ($entry in $plan) {
        if ($process.Path -eq $entry.Path -or $process.Path.StartsWith($entry.Path + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Удаляемые файлы используются процессом $($process.Id): $($process.Path)"
        }
    }
}
$deletedBytes = [long]0
$deletedFiles = 0
foreach ($entry in $plan) {
    if ($PSCmdlet.ShouldProcess($entry.Path, 'Удалить сгенерированные файлы')) {
        if ($entry.Directory) { Remove-Item -LiteralPath $entry.Path -Recurse -Force }
        else { Remove-Item -LiteralPath $entry.Path -Force }
        $deletedBytes += $entry.Bytes
        $deletedFiles += $entry.Files
    }
}
[pscustomobject]@{ PlannedTargets=$plan.Count; PlannedBytes=[long]($plan | Measure-Object -Property Bytes -Sum).Sum; DeletedFiles=$deletedFiles; DeletedBytes=$deletedBytes; PreservedInstaller=$keepSetup; PreservedBackups=$backups }
