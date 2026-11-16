param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Offline
)
. "$PSScriptRoot\common.ps1"
$solution = Join-Path $ProjectRoot 'StickyDesk.sln'
Restore-Project -ProjectPath $solution -Offline:$Offline
Invoke-ProjectDotnet -Arguments @('build', $solution, '-c', $Configuration, '--no-restore', '--nologo')
