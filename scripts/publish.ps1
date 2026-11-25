<#
.SYNOPSIS
    SnapDesk tek dosya (single-file) derleme ve masaüstü kısayolunu güncelleme betiği.
#>
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> SnapDesk derleme başlatılıyor..." -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host "==> Birim testleri çalıştırılıyor..." -ForegroundColor Yellow
    dotnet test "$root\tests\SnapDesk.Tests\SnapDesk.Tests.csproj" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Birim testleri başarısız oldu!"
    }
}

$outputDir = "$root\dist\SingleFile"
Write-Host "==> Single-file Release derlemesi oluşturuluyor -> $outputDir" -ForegroundColor Yellow

dotnet publish "$root\src\SnapDesk\SnapDesk.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Yayınlama başarısız oldu!"
}

$exePath = "$outputDir\SnapDesk.exe"
$icoPath = "$root\src\SnapDesk\app.ico"
$desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktopPath "SnapDesk.lnk"

Write-Host "==> Masaüstü kısayolu güncelleniyor: $shortcutPath" -ForegroundColor Yellow
if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
}
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $outputDir
$shortcut.Description = "SnapDesk - Modern Ekran Görüntüsü, Düzenleme ve Sabitleme Aracı"
$shortcut.IconLocation = "$exePath,0"
$shortcut.Save()

try {
    Add-Type -TypeDefinition '
    using System;
    using System.Runtime.InteropServices;
    public static class ShellRefresh {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }' -ErrorAction SilentlyContinue
    [ShellRefresh]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
} catch {
}

Write-Host "==> Başarılı! Masaüstü simgen hazır: $shortcutPath" -ForegroundColor Green
