# ============================================================
#  本地构建 MuSync 安装器（Inno Setup）
#  用法：powershell -ExecutionPolicy Bypass -File installer\build.ps1
#  前置：已安装 Inno Setup 6.5+
#        （winget install -e --id JRSoftware.InnoSetup）
#  产物：dist-setup\MuSync-Setup.exe
#        版本号默认取 MuSync.csproj 的 <Version>，也可用 -Version 覆盖
# ============================================================
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $versionMatch = Select-String -Path MuSync.csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $versionMatch) { throw "无法从 MuSync.csproj 读取版本号" }
    $Version = $versionMatch.Matches[0].Groups[1].Value
}
Write-Host "MuSync 安装器构建 — 版本 $Version"
Write-Host ""

Write-Host "[1/2] 发布 setup 载荷 → setup-payload\"
dotnet publish MuSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:MuSyncEdition=setup -o setup-payload
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }
Write-Host ""

Write-Host "[2/2] 编译安装器 → dist-setup\MuSync-Setup.exe"
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "未找到 ISCC.exe，请先安装 Inno Setup 6.5+" }
& $iscc "/DAppVersion=$Version" "installer\MuSync.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败" }
Write-Host ""
Write-Host "完成：dist-setup\MuSync-Setup.exe"
