param([Parameter(Mandatory = $true)][string]$ExecutablePath)
. "$PSScriptRoot\common.ps1"
$executable = Resolve-WorkspacePath $ExecutablePath
$markers = @(
    [pscustomobject]@{ Name='путь проекта'; Value=$ProjectRoot },
    [pscustomobject]@{ Name='путь пользовательского профиля'; Value=[Environment]::GetFolderPath('UserProfile') }
)
$email = git -C $ProjectRoot config user.email
if ($LASTEXITCODE -eq 0 -and $email -and $email -notmatch '@(users\.)?noreply\.github\.com$') {
    $markers += [pscustomobject]@{ Name='email автора'; Value=$email.Trim() }
}
$bytes = [IO.File]::ReadAllBytes($executable)
foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
    $decoded = $encoding.GetString($bytes)
    foreach ($marker in $markers) {
        if ($marker.Value -and $decoded.IndexOf($marker.Value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "В сборке обнаружен $($marker.Name). Файл не готов к публикации."
        }
    }
    $decoded = $null
}
$bytes = $null
[GC]::Collect()
Write-Host 'Проверка сборки: локальные пути и email автора не обнаружены.'
