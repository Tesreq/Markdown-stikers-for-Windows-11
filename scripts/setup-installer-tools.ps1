param()
. "$PSScriptRoot\common.ps1"
$version = '7.1.1'
$directory = Resolve-WorkspacePath ".local\tools\inno-setup-$version"
$compiler = Join-Path $directory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { Write-Host "Компилятор уже подготовлен: $compiler"; return }
$downloads = Resolve-WorkspacePath '.local\tools\downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$package = Join-Path $downloads "innosetup-$version-x64.exe"
if (!(Test-Path -LiteralPath $package)) {
    Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-$version-x64.exe" -OutFile $package -UseBasicParsing
}
$signature = Get-AuthenticodeSignature -LiteralPath $package
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0') {
    throw 'SHA-256 Inno Setup отличается от зафиксированной версии 7.1.1.'
}
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') {
    throw 'Цифровая подпись официального Inno Setup не подтверждена.'
}
$arguments = @('/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', ('/DIR="' + $directory + '"'))
$process = Start-Process -FilePath $package -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw "Подготовка компилятора завершилась с кодом $($process.ExitCode)." }
Write-Host "Компилятор подготовлен внутри проекта: $compiler"
