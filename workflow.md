# WORKFLOW CHUẨN DỰ ÁN MKLINK-GMTPC AVALONIA UI

> **Dự án**: MkLink-GMTPC (Standalone Windows Portable Path Mapper & NTFS Reparse Points Manager)  
> **Nền tảng**: .NET 8.0 Windows x64 (`net8.0-windows` / `win-x64`)  
> **Giao diện**: Avalonia UI 11.2 (FluentTheme Win11 Dark Mode)  
> **Repository**: [ghostminhtoan/mklink-avalonia](https://github.com/ghostminhtoan/mklink-avalonia)

---

## 1. NGUYÊN TẮC BẮT BUỘC TRONG MỌI PHIÊN LÀM VIỆC (NON-NEGOTIABLE)

1. **Ngôn ngữ phản hồi**: Luôn luôn trả lời bằng **tiếng Việt**.
2. **Kỷ luật trước khi code**:
   - BẮT BUỘC đọc kỹ file `workflow.md` này trước khi tiến hành viết code, phân tích hay chỉnh sửa dự án.
   - Luôn luôn kích hoạt và tuân thủ 2 skills: `teamwork-preview-protocol` và `prompt-optimizer` bất kể người dùng có tag hay không.
3. **Tiêu chuẩn Build**:
   - Mọi bản build bắt buộc xuất ra thư mục `publish/`.
   - Bắt buộc đạt **0 Lỗi (0 Error)** và **0 Cảnh báo (0 Warning)**.
   - Tuyệt đối không dừng lại khi chưa kiểm thử mở được giao diện ứng dụng (UI Verification).
4. **TỰ ĐỘNG UPLOAD BETA (QUY TẮC ĐẶC BIỆT BẮT BUỘC)**:
   - Kể cả khi tạo phiên chat mới và người dùng không nhắc đến từ khóa này, Agent **BẮT BUỘC LUÔN LUÔN TỰ ĐỘNG UPLOAD BETA** lên GitHub Release (tag `beta`) sau khi build thành công.
   - **TUYỆT ĐỐI KHÔNG ĐƯỢC QUÊN HOẶC CHỜ NGƯỜI DÙNG NHẮC!**
   - Chạy script `upload beta.cmd` (dùng Playwright Edge Direct-to-S3 tốc độ cao, không dùng REST API upload).
5. **Quy chuẩn Git & Commit**:
   - Tự động commit local sau khi hoàn thành công việc và đưa mã commit hash cho người dùng.
   - **Chỉ được phép commit local**, khi nào người dùng yêu cầu push GitHub thì mới thực hiện `git push`.
   - Danh tính tác giả Git: `user.name = GMTPC`, `user.email = ghostminhtoan@gmail.com`.
6. **Định dạng 3 dòng trạng thái cuối cùng**:
   ```text
   commit local: *mã hash*
   commit github: "không" (hoặc URL remote nếu người dùng yêu cầu push)
   path exe: C:\Users\Admin\source\repos\ghostminhtoan\Mklink all\MkLink .net avalonia\publish\MkLink-GMTPC.exe
   ```

---

## 2. QUY CHUẨN KIẾN TRÚC STANDALONE WINDOWS PORTABLE

Cấu trúc thư mục độc lập sau khi deploy vào `publish/`:
```text
publish/
├── .portable/
│   ├── runtimes/         <-- Toàn bộ DLL của Avalonia, SkiaSharp, .NET Runtime (~30 files)
│   └── settings/         <-- Cấu hình, history, last_md_folder.txt
├── MkLink-GMTPC.exe      <-- Launcher / Executable chính ở root
└── update_beta_restart.cmd (sinh tự động khi update)
```

- **Bootstrap Launcher (`MkLink.Launcher`)**:
  - Tập hợp file khởi động siêu nhẹ ở root `publish\MkLink-GMTPC.exe`.
  - Tự động cấu hình `SetDllDirectory` và `AssemblyLoadContext` nạp toàn bộ thư viện từ `.portable\runtimes\`.
  - **Tự động tải Runtimes khi thiếu**: Nếu chưa có `.portable\runtimes\`, launcher tự động kích hoạt `RuntimesDownloader` tải gói `runtimes.zip` từ `https://github.com/ghostminhtoan/mklink-avalonia/releases/tag/assets` với **16 kết nối HTTP Range song song** và tự động giải nén.

---

## 3. QUY CHUẨN GIAO DIỆN & TRẢI NGHIỆM NGƯỜI DÙNG

1. **Hệ thống 5 Tab cốt lõi**:
   - `COPY (Master)`: Nhận input, chuẩn hóa đường dẫn, sinh `MappedTarget`, cho phép sửa `EditOutputPath`, kéo thả phân cấp, sort, paste clipboard.
   - `DELETE (Mirror)`: Đồng bộ từ COPY, sinh lệnh xóa `rmdir /s /q` hoặc `del /f /q`.
   - `MKLINK D (Mirror)`: Đồng bộ từ COPY, sinh lệnh `mklink /D`.
   - `COPY REVERSE` & `DELETE REVERSE`: Luồng đảo ngược an toàn.
2. **Check Mklink Module**:
   - Quét trạng thái symlink thực tế bằng Win32 API / NTFS Reparse Points.
   - Phân loại trực quan: `MKLINK` (Cyan), `Real folder` (Gold), `Not exist` (Gray).
   - Thao tác hàng loạt: Copy + Delete + Mklink, Delete + Mklink, Make Reverse, Move to Destination.
3. **Bypass UIPI Admin Drag & Drop**:
   - Kể cả khi ứng dụng chạy với quyền Administrator (`High Integrity Level`), Windows Explorer (`Medium IL`) vẫn kéo thả files/folders vào ứng dụng mượt mà nhờ Win32 API:
     - `ChangeWindowMessageFilter` (WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA)
     - `ChangeWindowMessageFilterEx` trên HWND cửa sổ chính
     - `DragAcceptFiles(hwnd, true)`
4. **Tự động Cập nhật Beta trong App**:
   - Nút "Cập nhật Beta" trên Header cửa sổ chính.
   - Bấm vào tự động kiểm tra, tải bản phát hành mới từ GitHub, tạo bản sao lưu `zbackup_MkLink-GMTPC.exe`, ghi đè và khởi động lại.

---

## 4. QUY TRÌNH UPLOAD BETA (EDGE DIRECT-TO-S3 ACCELERATED)

- **Script thực thi**: `upload beta.cmd` gọi `upload_beta_browser.py`.
- **Phương thức truyền tải**: Sử dụng Microsoft Edge Automation (Playwright Persistent Profile `.edge_profile`) truyền tải trực tiếp lên AWS S3 accelerated endpoint của GitHub Releases (~100+ Mbps).
- **Quy tắc Kiểm soát Tốc độ (Speed Guard)**:
  - Sau 7 giây (`elapsed >= 7`), nếu tốc độ upload trung bình `< 10 Mbps`:
  - Script tự động gọi `window.stop()` để hủy luồng upload bị nghẽn.
  - Vòng lặp vô hạn (`while True:`) tự động thử lại cho đến khi upload thành công 100%.
- **Quy tắc Sao lưu Tự động (Backup Guard)**:
  - Khi đã có file `MkLink-GMTPC.exe` trên release `beta`:
  - Tự động xóa file `zbackup_MkLink-GMTPC.exe` cũ (nếu có).
  - Tự động đổi tên (rename) file `MkLink-GMTPC.exe` hiện tại thành `zbackup_MkLink-GMTPC.exe` qua PATCH metadata API trước khi upload file mới với tên gốc.

---

## 5. LỆNH VẬN HÀNH NHANH (CLI SHORTCUTS)

- **Build & Publish**:
  ```cmd
  build.cmd
  ```
- **Upload Release Beta**:
  ```cmd
  "upload beta.cmd"
  ```
- **Chạy thử bản Release**:
  ```powershell
  .\Run-Release.ps1
  ```
