# MkLink-GMTPC (Avalonia UI Windows Portable)

Ứng dụng quản lý và tạo Symlink / Reparse Points cho Windows, chuyển đổi sang Avalonia UI kế thừa trọn vẹn từ phiên bản .NET 4.8.

## Điểm Nổi Bật
- **Nền tảng**: .NET 8.0 Windows x64 (`win-x64`), giao diện Avalonia 11 Fluent Dark Theme chuẩn Windows 11.
- **Quy chuẩn Portable Độc lập trong folder `publish/`**:
  ```text
  publish/
  ├── .portable/
  │   ├── runtimes/         <-- Chứa 30 DLL của Avalonia, SkiaSharp, runtime dependencies
  │   └── settings/         <-- File cấu hình last_md_folder.txt, cache
  ├── MkLink-GMTPC.exe      <-- Launcher chính (174 KB)
  └── MkLink-GMTPC.runtimeconfig.json
  ```
- **Nghiệp vụ đầy đủ**:
  - 5 Tab luồng hoạt động: COPY (Master), DELETE (Mirror), MKLINK D (Mirror), COPY REVERSE & DELETE REVERSE.
  - Check Mklink Module: Quét trực tiếp NTFS Reparse Points qua Win32 API, phân loại MKLINK / Folder thật / Chưa có, so khớp đường dẫn.
  - Markdown State: Lưu & nạp cấu hình qua file `.md`, hỗ trợ Undo/Redo đa tầng.
  - Thực thi CMD Quản trị (Elevated CMD) minh bạch từng bước.

## Cách Build & Chạy
Chạy script PowerShell tự động build vào folder `publish/` và khởi chạy kiểm thử:
```powershell
.\Run-Release.ps1
```
