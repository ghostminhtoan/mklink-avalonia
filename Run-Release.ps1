param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Bắt đầu quy trình Build & Deploy MkLink-GMTPC (Avalonia Portable) ===" -ForegroundColor Cyan

# 1. Cưỡng chế đóng phiên bản ứng dụng cũ đang chạy nếu có
try {
    & cmd.exe /c "taskkill /F /IM MkLink-GMTPC.exe /T" > $null 2>&1
} catch {
}

# 2. Chuẩn bị thư mục portable
$portableDir = Join-Path $ProjectRoot ".portable"
$runtimesDir = Join-Path $portableDir "runtimes"
$settingsDir = Join-Path $portableDir "settings"

if (-not (Test-Path $runtimesDir)) {
    New-Item -ItemType Directory -Path $runtimesDir -Force | Out-Null
}
if (-not (Test-Path $settingsDir)) {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
}

$appCsproj = Join-Path $ProjectRoot "src\MkLink.App\MkLink.App.csproj"
$launcherCsproj = Join-Path $ProjectRoot "src\MkLink.Launcher\MkLink.Launcher.csproj"

# 3. Publish MkLink.App vào .portable/runtimes
Write-Host "1. Đang publish MkLink.App vào .portable/runtimes..." -ForegroundColor Yellow
& dotnet publish $appCsproj -c Release -r win-x64 --no-self-contained -o $runtimesDir --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish MkLink.App thất bại với mã lỗi $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 4. Publish Launcher MkLink-GMTPC.exe
Write-Host "2. Đang publish Launcher MkLink-GMTPC.exe..." -ForegroundColor Yellow
$tempOut = Join-Path $ProjectRoot "artifacts_temp"
if (Test-Path $tempOut) {
    Remove-Item -Recurse -Force $tempOut
}

& dotnet publish $launcherCsproj -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained -o $tempOut --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish MkLink.Launcher thất bại với mã lỗi $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 5. Đưa MkLink-GMTPC.exe và runtimeconfig ra root
$launcherExe = Join-Path $tempOut "MkLink-GMTPC.exe"
$destExe = Join-Path $ProjectRoot "MkLink-GMTPC.exe"
Copy-Item $launcherExe -Destination $destExe -Force

$launcherConfig = Join-Path $tempOut "MkLink-GMTPC.runtimeconfig.json"
$destConfig = Join-Path $ProjectRoot "MkLink-GMTPC.runtimeconfig.json"
if (Test-Path $launcherConfig) {
    Copy-Item $launcherConfig -Destination $destConfig -Force
}

Remove-Item -Recurse -Force $tempOut

Write-Host "=== Build & Deploy thành công 100%! ===" -ForegroundColor Green
Write-Host "Executable: $destExe" -ForegroundColor Green
Write-Host "Runtimes:   $runtimesDir" -ForegroundColor Green
Write-Host "Settings:   $settingsDir" -ForegroundColor Green

# 6. Khởi chạy ứng dụng
Write-Host "3. Đang khởi chạy MkLink-GMTPC.exe..." -ForegroundColor Cyan
Start-Process -FilePath $destExe -WorkingDirectory $ProjectRoot
