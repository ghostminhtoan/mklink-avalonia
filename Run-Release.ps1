param(
    [string]$ProjectRoot = $PSScriptRoot,
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'

Write-Host "=== Bat dau quy trinh Build & Publish MkLink-GMTPC (Avalonia Portable) ===" -ForegroundColor Cyan

# 1. Cuong che dong phien ban ung dung cu dang chay neu co
try {
    & cmd.exe /c "taskkill /F /IM MkLink-GMTPC.exe /T" > $null 2>&1
} catch {
}

# 2. Chuan bi thu muc publish chinh
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

# 3. Publish MkLink.App vao publish/.portable/runtimes
Write-Host "1. Dang publish MkLink.App vao publish/.portable/runtimes..." -ForegroundColor Yellow
& dotnet publish $appCsproj -c Release -r win-x64 --no-self-contained -o $publishRuntimesDir --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish MkLink.App that bai voi ma loi $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 4. Publish Launcher MkLink-GMTPC.exe
Write-Host "2. Dang publish Launcher MkLink-GMTPC.exe..." -ForegroundColor Yellow
$tempOut = Join-Path $ProjectRoot "artifacts_temp"
if (Test-Path $tempOut) {
    Remove-Item -Recurse -Force $tempOut
}

& dotnet publish $launcherCsproj -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained -o $tempOut --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish MkLink.Launcher that bai voi ma loi $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 5. Dua MkLink-GMTPC.exe va runtimeconfig vao publish
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

# 6. Dong bo sang thu muc root de chay duoc o ca root lan publish
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

Write-Host "=== Build & Publish thanh cong 100%! ===" -ForegroundColor Green
Write-Host "Publish Folder: $publishDir" -ForegroundColor Green
Write-Host "Executable:     $destExe" -ForegroundColor Green
Write-Host "Runtimes:       $publishRuntimesDir" -ForegroundColor Green
Write-Host "Settings:       $publishSettingsDir" -ForegroundColor Green

# 7. Kiem thu tu dong mo ung dung va xac nhan giao dien UI hoat dong neu khong co flag SkipLaunch
if (-not $SkipLaunch) {
    Write-Host "3. Dang kiem thu khoi chay MkLink-GMTPC.exe tu folder publish..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $destExe -WorkingDirectory $publishDir -PassThru
    Start-Sleep -Milliseconds 2500

    if ($proc.HasExited) {
        Write-Error "Ung dung thoat bat thuong voi ExitCode: $($proc.ExitCode)"
        exit 1
    } else {
        Write-Host "==> XAC NHAN THANH CONG: Giao dien UI MkLink-GMTPC dang chay muot ma (PID: $($proc.Id))!" -ForegroundColor Green
    }
}
