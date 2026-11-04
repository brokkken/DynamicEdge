<#
  Собирает Dynamic Edge и упаковывает в подписанный MSIX.
  Требуется: Windows 10/11, .NET 10 SDK, Windows SDK (makeappx/signtool — есть в Visual Studio / Windows SDK).
  Запуск:  powershell -ExecutionPolicy Bypass -File build\package.ps1
  Результат: artifacts\DynamicEdge-Setup.exe (установщик), DynamicEdge.msix, DynamicEdge.cer, install.ps1
#>
param([string]$Version = "1.0.0.0")
$ErrorActionPreference = "Stop"
$root   = Split-Path $PSScriptRoot -Parent
$art    = Join-Path $root "artifacts"
$pub    = Join-Path $art "publish"
$layout = Join-Path $art "layout"
Remove-Item $art -Recurse -Force -ErrorAction SilentlyContinue
New-Item $art, $pub, $layout -ItemType Directory | Out-Null

Write-Host "==> dotnet publish" -ForegroundColor Cyan
dotnet publish "$root\src\DynamicEdge\DynamicEdge.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:Version=$($Version -replace '\.0$','') -o $pub
if ($LASTEXITCODE) { throw "publish failed" }

Write-Host "==> staging MSIX layout" -ForegroundColor Cyan
Copy-Item "$pub\*" $layout -Recurse
New-Item "$layout\Assets" -ItemType Directory -Force | Out-Null
Copy-Item "$root\src\DynamicEdge\Assets\*.png" "$layout\Assets\"
(Get-Content "$PSScriptRoot\AppxManifest.xml" -Raw).Replace("__VERSION__", $Version) | Set-Content "$layout\AppxManifest.xml" -Encoding UTF8

function Find-SdkTool($name) {
    $c = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $c) { throw "$name не найден. Установите Windows SDK." }
    $c.FullName
}
$makeappx = Find-SdkTool "makeappx.exe"
$signtool = Find-SdkTool "signtool.exe"

Write-Host "==> makeappx" -ForegroundColor Cyan
$msix = Join-Path $art "DynamicEdge.msix"
& $makeappx pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE) { throw "makeappx failed" }

Write-Host "==> self-signed certificate + sign" -ForegroundColor Cyan
$pwd  = ConvertTo-SecureString "dynamicedge" -AsPlainText -Force
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=DynamicEdge" -KeyUsage DigitalSignature `
        -FriendlyName "Dynamic Edge" -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pfx = Join-Path $art "DynamicEdge.pfx"
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $art "DynamicEdge.cer") | Out-Null
& $signtool sign /fd SHA256 /f $pfx /p dynamicedge $msix
if ($LASTEXITCODE) { throw "signtool failed" }
Remove-Item $pfx
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
Copy-Item "$PSScriptRoot\install.ps1" $art

Write-Host "==> Inno Setup (DynamicEdge-Setup.exe)" -ForegroundColor Cyan
$iscc = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    & $iscc "/DAppVersion=$Version" "/DArtDir=$art" "$PSScriptRoot\installer.iss"
    if ($LASTEXITCODE) { throw "Inno Setup failed" }
} else {
    Write-Warning "Inno Setup 6 не найден — DynamicEdge-Setup.exe не создан (установите: winget install JRSoftware.InnoSetup)."
}
Remove-Item $layout -Recurse -Force
Write-Host "`nГотово: $msix" -ForegroundColor Green
Write-Host "Установка: правый клик на artifacts\install.ps1 → Выполнить с помощью PowerShell (нужны права администратора)."
