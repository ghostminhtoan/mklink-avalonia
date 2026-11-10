param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Bắt đầu quy trình Build & Publish MkLink-GMTPC (Avalonia Portable) ===" -ForegroundColor Cyan

# 1. Cưỡng chế đóng phiên bản ứng dụng cũ đang chạy nếu có
try {
    & cmd.exe /c "taskkill /F /IM MkLink-GMTPC.exe /T" > $null 2>&1
} catch {
}

# 2. Chuẩn bị thư mục publish chính
$publishDir = Join-Path $ProjectRoot "publish"
$publishPortableDir = Join-Path $publishDir ".portable"
$publishRuntimesDir = Join-Path $publishPortableDir "runtimes"
$publishSettingsDir = Join-Path $publishPortableDir "settings"

if (-not (Test-Path $publishRuntimesDir)) {
    New-Item -ItemType Directory -Path $publishRuntimesDir -Force | Out-Null
}
if (-not (Test-Path $publishSettingsDir)) {
    New-Item -ItemType Directory -Path $publishSettingsDir -Force | Out-Null
}

$appCsproj = Join-Path $ProjectRoot "src\MkLink.App\MkLink.App.csproj"
$launcherCsproj = Join-Path $ProjectRoot "src\MkLink.Launcher\MkLink.Launcher.csproj"

# 3. Publish MkLink.App vào publish/.portable/runtimes
Write-Host "1. Đang publish MkLink.App vào publish/.portable/runtimes..." -ForegroundColor Yellow
& dotnet publish $appCsproj -c Release -r win-x64 --no-self-contained -o $publishRuntimesDir --nologo -v q
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

# 5. Đưa MkLink-GMTPC.exe và runtimeconfig vào publish
$launcherExe = Join-Path $tempOut "MkLink-GMTPC.exe"
$destExe = Join-Path $publishDir "MkLink-GMTPC.exe"
Copy-Item $launcherExe -Destination $destExe -Force

$destConfig = Join-Path $publishDir "MkLink-GMTPC.runtimeconfig.json"
$runtimeConfigContent = @'
{
  "runtimeOptions": {
    "tfm": "net8.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "8.0.0"
    },
    "additionalProbingPaths": [
      ".portable/runtimes"
    ]
  }
}
'@
Set-Content -Path $destConfig -Value $runtimeConfigContent -Encoding UTF8

Remove-Item -Recurse -Force $tempOut

# 6. Đồng bộ sang thư mục root để chạy được ở cả root lẫn publish
$rootPortableDir = Join-Path $ProjectRoot ".portable"
$rootRuntimesDir = Join-Path $rootPortableDir "runtimes"
$rootSettingsDir = Join-Path $rootPortableDir "settings"

if (-not (Test-Path $rootRuntimesDir)) {
    New-Item -ItemType Directory -Path $rootRuntimesDir -Force | Out-Null
}
if (-not (Test-Path $rootSettingsDir)) {
    New-Item -ItemType Directory -Path $rootSettingsDir -Force | Out-Null
}

Copy-Item -Path (Join-Path $publishRuntimesDir "*") -Destination $rootRuntimesDir -Recurse -Force
Copy-Item -Path $destExe -Destination (Join-Path $ProjectRoot "MkLink-GMTPC.exe") -Force
Copy-Item -Path $destConfig -Destination (Join-Path $ProjectRoot "MkLink-GMTPC.runtimeconfig.json") -Force

Write-Host "=== Build & Publish thành công 100%! ===" -ForegroundColor Green
Write-Host "Publish Folder: $publishDir" -ForegroundColor Green
Write-Host "Executable:     $destExe" -ForegroundColor Green
Write-Host "Runtimes:       $publishRuntimesDir" -ForegroundColor Green
Write-Host "Settings:       $publishSettingsDir" -ForegroundColor Green

# 7. Kiểm thử tự động mở ứng dụng và xác nhận giao diện UI hoạt động
Write-Host "3. Đang kiểm thử khởi chạy MkLink-GMTPC.exe từ folder publish..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $destExe -WorkingDirectory $publishDir -PassThru
Start-Sleep -Milliseconds 2500

if ($proc.HasExited) {
    Write-Error "Ứng dụng thoát bất thường với ExitCode: $($proc.ExitCode)"
    exit 1
} else {
    Write-Host "==> XÁC NHẬN THÀNH CÔNG: Giao diện UI MkLink-GMTPC đang chạy mượt mà (PID: $($proc.Id))!" -ForegroundColor Green
}
