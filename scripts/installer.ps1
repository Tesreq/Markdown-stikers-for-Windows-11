param(
    [switch]$Offline,
    [switch]$NoPublish,
    [string]$CompilerPath
)
. "$PSScriptRoot\common.ps1"
$payload = Resolve-WorkspacePath '.local\installer-payload'
$output = $ProjectRoot
$project = Join-Path $ProjectRoot 'src\StickyDesk.App\StickyDesk.App.csproj'
$version = ([xml](Get-Content -Encoding UTF8 -Raw -LiteralPath $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (!$version) { throw 'Версия приложения не найдена.' }
if (!$CompilerPath) {
    $candidates = @(
        (Join-Path $ProjectRoot '.local\tools\inno-setup-7.1.1\ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
    )
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) {
    throw 'Нужен компилятор Inno Setup 7. Выполните scripts/setup-installer-tools.ps1 либо передайте -CompilerPath.'
}
if (!(Test-Path -LiteralPath (Join-Path $ProjectRoot 'src\StickyDesk.App\Assets\StickyDesk.ico'))) {
    & "$PSScriptRoot\create-icon.ps1"
}
if (!$NoPublish) { & "$PSScriptRoot\publish.ps1" -Offline:$Offline -OutputDirectory $payload }
$executable = Join-Path $payload 'StickyDesk.exe'
if (!(Test-Path -LiteralPath $executable) -or (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -notlike "$version*") {
    throw 'Автономная сборка отсутствует или имеет другую версию.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $CompilerPath "/DAppVersion=$version" "/DSourceDir=$payload" "/DProjectRoot=$ProjectRoot" "/DInstallerOutput=$output" (Join-Path $ProjectRoot 'installer\StickyDesk.iss')
if ($LASTEXITCODE -ne 0) { throw "Компилятор Inno Setup завершился с кодом $LASTEXITCODE." }
$setup = Join-Path $output "StickyDesk-Setup-$version-x64.exe"
Write-Host "Установщик: $setup"
