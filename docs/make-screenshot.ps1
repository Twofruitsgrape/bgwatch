param(
    [string]$Exe = 'E:\Projects\bgwatch\dist\bgwatch.exe',
    [string]$Out = 'E:\Projects\bgwatch\docs\screenshot.png'
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Cap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[Cap]::SetProcessDPIAware() | Out-Null

New-Item -ItemType Directory -Path (Split-Path -Parent $Out) -Force | Out-Null

$p = Start-Process -FilePath $Exe -PassThru
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 140; $i++) {
    Start-Sleep -Milliseconds 50
    $p.Refresh()
    if ($p.MainWindowHandle -ne 0) { $hwnd = $p.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { throw "拿不到窗口" }

# 等扫描完成（状态栏不再显示"扫描中"），最多 15 秒
Start-Sleep -Seconds 7
[Cap]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800

$r = New-Object Cap+RECT
[Cap]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Output ("窗口尺寸: {0}x{1}" -f $w, $h)

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Cap]::PrintWindow($hwnd, $hdc, 2)     # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()

if (-not $ok) {
    Write-Output "PrintWindow 失败，改用屏幕抓取"
    $bmp.Dispose()
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g2 = [System.Drawing.Graphics]::FromImage($bmp)
    $g2.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g2.Dispose()
}

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output ("已保存: {0}  ({1:N1} KB)" -f $Out, ((Get-Item $Out).Length / 1KB))
