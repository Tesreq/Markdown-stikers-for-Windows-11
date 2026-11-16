$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Resolve-WorkspacePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $resolved = if ([IO.Path]::IsPathRooted($Path)) { [IO.Path]::GetFullPath($Path) } else { [IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path)) }
    if (!$resolved.StartsWith($ProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Путь должен находиться внутри проекта: $resolved"
    }
    $ancestor = $resolved
    while ($ancestor.Length -gt $ProjectRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -Force -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Пути через символические ссылки и junction не поддерживаются: $ancestor"
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $resolved
}

function Invoke-ProjectDotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet завершился с кодом $LASTEXITCODE." }
}

function Restore-Project {
    param([string]$ProjectPath, [switch]$Offline, [string[]]$AdditionalArguments = @())
    $restoreArguments = @('restore', $ProjectPath, '--nologo') + $AdditionalArguments
    if ($Offline) {
        $cache = Resolve-WorkspacePath '.local\nuget\packages'
        if (!(Test-Path -LiteralPath $cache)) { throw 'Локальный кэш отсутствует. Выполните первую сборку без -Offline.' }
        $restoreArguments += @('--source', $cache)
    }
    Invoke-ProjectDotnet -Arguments $restoreArguments
}
