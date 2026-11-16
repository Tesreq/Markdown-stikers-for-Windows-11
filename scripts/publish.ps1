param(
    [switch]$FrameworkDependent,
    [switch]$Offline,
    [string]$OutputDirectory = 'artifacts\StickyDesk-win-x64'
)
. "$PSScriptRoot\common.ps1"
$project = Join-Path $ProjectRoot 'src\StickyDesk.App\StickyDesk.App.csproj'
$output = Resolve-WorkspacePath $OutputDirectory
$existingExecutable = Join-Path $output 'StickyDesk.exe'
if (Test-Path -LiteralPath $existingExecutable) {
    try {
        $probe = [IO.File]::Open($existingExecutable, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $probe.Dispose()
    } catch [IO.IOException] {
        throw 'Сборка используется запущенным приложением. Завершите StickyDesk через трей либо укажите другую папку через -OutputDirectory.'
    }
}
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$properties = @('-r', 'win-x64', "-p:SelfContained=$selfContained")
if (!$FrameworkDependent) { $properties += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }
Restore-Project -ProjectPath $project -Offline:$Offline -AdditionalArguments $properties
$publishArguments = @('publish', $project, '-c', 'Release', '--no-restore', '--nologo', '-o', $output) + $properties
Invoke-ProjectDotnet -Arguments $publishArguments
Write-Host "Готово: $output\StickyDesk.exe"
