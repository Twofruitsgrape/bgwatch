<#
    bgwatch 打包

    用法：
      powershell -ExecutionPolicy Bypass -File build.ps1                    # 只有主程序
      powershell -ExecutionPolicy Bypass -File build.ps1 -Installer         # 额外生成安装包
      powershell -ExecutionPolicy Bypass -File build.ps1 -Installer -DownloadNsis   # 自动下载 NSIS
      powershell -ExecutionPolicy Bypass -File build.ps1 -Portable          # 自带运行时（约 150MB）

    产物：
      dist\bgwatch.exe                  主程序（单文件，约 0.85MB）
      dist\bgwatch-1.0.0-setup.exe      安装包（NSIS，约 0.36MB）
#>

[CmdletBinding()]
param(
    [switch]$Portable,
    [switch]$Installer,
    [switch]$DownloadNsis,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\BgWatch\BgWatch.csproj'
$dist = Join-Path $root 'dist'
$nsisVer = '3.10'

Write-Host "==> 清理 dist" -ForegroundColor Cyan
if (Test-Path $dist) {
    Get-ChildItem $dist -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

# ---------------------------------------------------------------- 主程序
$selfContained = if ($Portable) { 'true' } else { 'false' }
Write-Host ("==> 发布主程序（self-contained={0}）" -f $selfContained) -ForegroundColor Cyan

$publishArgs = @(
    $proj, '-c', $Configuration, '-r', 'win-x64',
    '--self-contained', $selfContained,
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=none',
    '-o', $dist, '--nologo', '-v', 'minimal'
)
if ($Portable) { $publishArgs += '-p:EnableCompressionInSingleFile=true' }

dotnet publish @publishArgs

$exe = Join-Path $dist 'bgwatch.exe'
if (-not (Test-Path $exe)) { throw "发布失败：找不到 $exe" }

# 只清 pdb；deps.json / runtimeconfig.json 必须保留
Get-ChildItem $dist -File -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue
$appSize = [math]::Round((Get-Item $exe).Length / 1MB, 2)

# ---- 冒烟测试 ----
Write-Host "==> 冒烟测试（--dump）" -ForegroundColor Cyan
$dump = Join-Path $dist '_smoke.txt'
$proc = Start-Process -FilePath $exe -ArgumentList @('--dump', $dump) -Wait -PassThru
if ($proc.ExitCode -ne 0) { throw "冒烟测试失败：退出码 $($proc.ExitCode)" }
if (-not (Test-Path $dump)) { throw "冒烟测试失败：--dump 没有产出" }
$first = Get-Content $dump -TotalCount 1 -Encoding UTF8
$len = (Get-Item $dump).Length
Remove-Item $dump -Force
if ($len -lt 100 -or $first -notmatch 'MB') { throw "冒烟测试失败：输出异常 —— $first" }
Write-Host ("    {0}" -f $first) -ForegroundColor Green

# ---------------------------------------------------------------- 安装包
$setupPath = Join-Path $dist ("bgwatch-1.0.0-setup.exe")

if ($Installer) {
    Write-Host "==> 构建安装包（NSIS）" -ForegroundColor Cyan

    function Find-Makensis {
        $candidates = @(
            (Join-Path $root 'tools\nsis\Bin\makensis.exe'),
            (Join-Path $root 'tools\nsis\makensis.exe'),
            (Join-Path $env:ProgramFiles 'NSIS\makensis.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe')
        )
        foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
        return $null
    }

    $makensis = Find-Makensis

    if (-not $makensis -and $DownloadNsis) {
        Write-Host "    下载 NSIS $nsisVer …" -ForegroundColor DarkGray
        $zip = Join-Path $env:TEMP "nsis-$nsisVer.zip"
        $url = "https://downloads.sourceforge.net/project/nsis/NSIS%203/$nsisVer/nsis-$nsisVer.zip"
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        $tools = Join-Path $root 'tools'
        New-Item -ItemType Directory -Path $tools -Force | Out-Null
        Expand-Archive -Path $zip -DestinationPath $tools -Force
        $extracted = Join-Path $tools "nsis-$nsisVer"
        if (Test-Path (Join-Path $tools 'nsis')) { Remove-Item (Join-Path $tools 'nsis') -Recurse -Force }
        Rename-Item $extracted 'nsis'
        Remove-Item $zip -Force
        $makensis = Find-Makensis
    }

    if (-not $makensis) {
        Write-Host "    找不到 makensis.exe" -ForegroundColor Yellow
        Write-Host "      办法一：加 -DownloadNsis 让脚本自动下载" -ForegroundColor Yellow
        Write-Host "      办法二：装 NSIS（https://nsis.sourceforge.io），或把便携版解压到 tools\nsis\" -ForegroundColor Yellow
        throw "安装包未生成"
    }

    $nsi = Join-Path $root 'installer\installer.nsi'
    if (-not (Test-Path $nsi)) { throw "找不到 $nsi" }

    & $makensis /V2 /INPUTCHARSET UTF8 $nsi
    if ($LASTEXITCODE -ne 0) { throw "makensis 返回 $LASTEXITCODE" }
    if (-not (Test-Path $setupPath)) { throw "安装包未生成：$setupPath" }

    $setupSize = [math]::Round((Get-Item $setupPath).Length / 1MB, 2)
}

# ---------------------------------------------------------------- 校验文件
# 没签名的情况下，至少让人能验证文件没被改过
$hashLines = Get-ChildItem $dist -File -Filter *.exe | Sort-Object Name | ForEach-Object {
    $h = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    "{0}  {1}" -f $h, $_.Name
}
if ($hashLines) {
    $sumFile = Join-Path $dist 'SHA256SUMS.txt'
    $hashLines | Set-Content -Path $sumFile -Encoding ASCII
    Write-Host ""
    Write-Host "SHA256SUMS.txt:" -ForegroundColor Cyan
    $hashLines | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
}

# ---------------------------------------------------------------- 汇总
Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Write-Host ("  dist\bgwatch.exe                {0} MB" -f $appSize)
if ($Installer) {
    Write-Host ("  dist\bgwatch-1.0.0-setup.exe    {0} MB" -f $setupSize)
}
if (-not $Portable) {
    Write-Host "  需要 .NET 10 桌面运行时（x64）。想零依赖就加 -Portable。" -ForegroundColor DarkGray
}
