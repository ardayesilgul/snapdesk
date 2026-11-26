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
$exePath = "$outputDir\SnapDesk.exe"

Get-Process SnapDesk -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

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

$desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$startMenuPrograms = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$shortcutPaths = @(
    (Join-Path $desktopPath "SnapDesk.lnk"),
    (Join-Path $startMenuPrograms "SnapDesk.lnk")
)

$wsh = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in $shortcutPaths) {
    Write-Host "==> Kısayol güncelleniyor: $shortcutPath" -ForegroundColor Yellow
    $dir = Split-Path -Parent $shortcutPath
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if (Test-Path $shortcutPath) {
        Remove-Item $shortcutPath -Force
    }
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $outputDir
    $shortcut.Description = "SnapDesk - Ekran Görüntüsü, Düzenleme ve Sabitleme Aracı"
    $shortcut.IconLocation = "$exePath,0"
    $shortcut.Save()
}

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

$desktopName = "Default"
try {
    Add-Type -TypeDefinition '
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    public static class SnapDeskWinStation {
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int needed);
        public static string DesktopName() {
            int n;
            var sb = new StringBuilder(256);
            if (GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, sb, 256, out n)) {
                return sb.ToString();
            }
            return "Default";
        }
    }' -ErrorAction SilentlyContinue
    $desktopName = [SnapDeskWinStation]::DesktopName()
} catch {
}

if ($desktopName -eq "Default") {
    Write-Host "==> SnapDesk sistem tepsisinde başlatılıyor..." -ForegroundColor Yellow
    Start-Process -FilePath $exePath -ArgumentList "--tray" -WorkingDirectory $outputDir
    Write-Host "==> Başarılı! Masaüstü ve Başlat Menüsü kısayolları hazır, SnapDesk sistem tepsisinde çalışıyor." -ForegroundColor Green
} else {
    Write-Host "==> Masaüstü '$desktopName' izole olduğu için otomatik başlatma atlandı; kullanıcı masaüstünden açılabilir." -ForegroundColor Cyan
    Write-Host "==> Başarılı! Masaüstü ve Başlat Menüsü kısayolları hazır: $desktopPath\SnapDesk.lnk" -ForegroundColor Green
}
