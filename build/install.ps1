# Устанавливает Dynamic Edge: доверяет сертификату издателя и ставит MSIX. Запускать из папки с DynamicEdge.msix и .cer.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    exit
}
Import-Certificate -FilePath (Join-Path $here "DynamicEdge.cer") -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
Add-AppxPackage -Path (Join-Path $here "DynamicEdge.msix") -ForceUpdateFromAnyVersion
Write-Host "Dynamic Edge установлен. Запустите его из меню «Пуск»." -ForegroundColor Green
Start-Sleep 3
