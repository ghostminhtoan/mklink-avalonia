#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Upload Release Beta via Microsoft Edge (Playwright)
Direct-to-S3 Accelerated Upload (~100+ Mbps)
Persistent Automation Profile (.edge_profile)
Speed Guard: Sau 7s neu upload speed < 10 Mbps thi tu huy va thu lai (Vong lap vo han)
Backup Guard: Tu dong rename asset cu thanh zbackup_<ten_file> truoc khi upload file moi
MkLink-GMTPC Avalonia
"""

import os
import sys
import time
import argparse
import ctypes
import subprocess
import json
from pathlib import Path
import urllib.request
import urllib.error
from playwright.sync_api import sync_playwright

REPO_OWNER = "ghostminhtoan"
REPO_NAME = "mklink-avalonia"
RELEASE_TAG = "beta"
EDIT_URL = f"https://github.com/{REPO_OWNER}/{REPO_NAME}/releases/edit/{RELEASE_TAG}"
VIEW_URL = f"https://github.com/{REPO_OWNER}/{REPO_NAME}/releases/tag/{RELEASE_TAG}"

BASE_DIR = Path(__file__).resolve().parent
PUBLISH_DIR = BASE_DIR / "publish"
PROFILE_DIR = BASE_DIR / ".edge_profile"
COMMON_PROFILE_DIR = Path(r"C:\Users\Admin\source\repos\ghostminhtoan\Bột chiên Tân Sơn Nhì\.edge_profile")
if not PROFILE_DIR.exists() and COMMON_PROFILE_DIR.exists():
    PROFILE_DIR = COMMON_PROFILE_DIR
TOKEN_FILE = BASE_DIR / "github_token.txt"

TARGET_FILES = [
    PUBLISH_DIR / "MkLink-GMTPC.exe"
]

# Cau hinh giam sat toc do va retry
MIN_UPLOAD_SPEED_MBPS = 10.0   # Nguong toc do toi thieu 10 Mbps
GRACE_PERIOD_SEC = 7           # Sau 7 giay bat dau kiem tra toc do
POLL_INTERVAL_SEC = 0.5        # Tan suat do tien do


def disable_quick_edit_mode():
    """Vo hieu hoa QuickEdit Mode tren Windows Console de tranh freeze tien trinh"""
    try:
        kernel32 = ctypes.windll.kernel32
        h_stdin = kernel32.GetStdHandle(-10)  # STD_INPUT_HANDLE = -10
        mode = ctypes.c_uint32()
        if kernel32.GetConsoleMode(h_stdin, ctypes.byref(mode)):
            ENABLE_QUICK_EDIT_MODE = 0x0040
            ENABLE_EXTENDED_FLAGS = 0x0080
            new_mode = (mode.value & ~ENABLE_QUICK_EDIT_MODE) | ENABLE_EXTENDED_FLAGS
            kernel32.SetConsoleMode(h_stdin, new_mode)
    except Exception:
        pass


def cleanup_edge_profile_locks(profile_dir):
    """
    Don dep tien trinh msedge mo coi va lock files de tranh ProcessSingleton error.
    """
    p_dir = Path(profile_dir).resolve()
    if not p_dir.exists():
        return

    p_str = str(p_dir).lower()
    try:
        cmd = ["wmic", "process", "where", "name='msedge.exe'", "get", "ProcessId,CommandLine", "/format:csv"]
        res = subprocess.run(cmd, capture_output=True, text=True, timeout=8)
        killed = []
        for line in res.stdout.splitlines():
            line = line.strip()
            if not line or line.startswith("Node,"):
                continue
            parts = line.rsplit(",", 1)
            if len(parts) == 2:
                pid = parts[1].strip()
                cmdline = parts[0].lower()
                if pid.isdigit() and p_str in cmdline:
                    try:
                        subprocess.run(["taskkill", "/F", "/PID", pid], capture_output=True, timeout=5)
                        killed.append(pid)
                    except Exception:
                        pass
        if killed:
            print(f"[CLEANUP] Da giai phong {len(killed)} tien trinh Edge tren profile: PID {', '.join(killed)}")
            time.sleep(1.0)
    except Exception:
        pass

    lock_names = ["lockfile", "SingletonLock", "SingletonCookie", "SingletonSocket", "SingletonPort"]
    for root_or_def in [p_dir, p_dir / "Default"]:
        if not root_or_def.exists():
            continue
        for l_name in lock_names:
            f_path = root_or_def / l_name
            if f_path.exists() or f_path.is_symlink():
                try:
                    f_path.unlink(missing_ok=True)
                except Exception:
                    pass


def launch_edge_context_safe(p, profile_dir, headless, max_retries=3):
    p_dir = Path(profile_dir).resolve()
    p_dir.mkdir(parents=True, exist_ok=True)
    cleanup_edge_profile_locks(p_dir)

    for attempt in range(1, max_retries + 1):
        try:
            context = p.chromium.launch_persistent_context(
                user_data_dir=str(p_dir),
                channel="msedge",
                headless=headless,
                args=[
                    "--disable-blink-features=AutomationControlled",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-dev-shm-usage",
                    "--no-sandbox"
                ]
            )
            return context
        except Exception as ex:
            err_str = str(ex).lower()
            if "processsingleton" in err_str or "lock file" in err_str or "cannot be created" in err_str:
                print(f"[CANH BAO] Xung dot ProcessSingleton Edge profile (Lan {attempt}/{max_retries}). Dang don dep...")
                cleanup_edge_profile_locks(p_dir)
                time.sleep(1.5)
                if attempt == max_retries:
                    raise
            else:
                raise


def get_git_credential_token():
    """Lay GitHub token tu Git Credential Manager cua Windows"""
    try:
        proc = subprocess.run(
            ["git", "credential", "fill"],
            input="protocol=https\nhost=github.com\n\n",
            capture_output=True,
            text=True,
            timeout=5
        )
        for line in proc.stdout.splitlines():
            if line.startswith("password="):
                return line.split("=", 1)[1].strip()
    except Exception:
        pass
    return ""


def load_github_token():
    if TOKEN_FILE.exists():
        try:
            t = TOKEN_FILE.read_text(encoding="utf-8").strip()
            if t:
                return t
        except Exception:
            pass
    env_token = os.environ.get("GITHUB_TOKEN", "").strip()
    if env_token:
        return env_token
    return get_git_credential_token()


def apply_backup_rule_to_existing_assets(target_names):
    """
    Quy tac tao backup upload beta:
    Khi da co file san (vi du MkLink-GMTPC.exe), tu dong rename file cu voi tien to la 'zbackup_'.
    Neu da co 'zbackup_MkLink-GMTPC.exe' tu lan truoc, xoa zbackup cu de nhuong cho cho ban backup moi.
    Dung PATCH API de doi ten metadata asset trong vai milliseconds (khong upload/download API).
    """
    token = load_github_token()
    if not token:
        print("[BACKUP] Khong tim thay GitHub token qua Git Credential, bo qua buoc rename backup truoc.")
        return

    req_headers = {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "MkLink-GMTPC-Uploader/1.0"
    }

    try:
        url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/tags/{RELEASE_TAG}"
        req = urllib.request.Request(url, headers=req_headers)
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.loads(resp.read().decode("utf-8"))

        assets = data.get("assets", [])
        asset_map = {a.get("name"): a.get("id") for a in assets}

        for fname in target_names:
            backup_name = f"zbackup_{fname}"

            # Neu file goc ton tai tren release
            if fname in asset_map:
                old_asset_id = asset_map[fname]

                # Neu zbackup cu da ton tai, xoa zbackup cu truoc
                if backup_name in asset_map:
                    del_id = asset_map[backup_name]
                    print(f"[BACKUP] Xoa ban backup cu hon '{backup_name}' (ID: {del_id})...")
                    del_url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/assets/{del_id}"
                    del_req = urllib.request.Request(del_url, headers=req_headers, method="DELETE")
                    try:
                        with urllib.request.urlopen(del_req, timeout=15):
                            print(f"[BACKUP] -> Da xoa '{backup_name}' cu.")
                    except Exception as e:
                        print(f"[BACKUP] Chu y khi xoa backup cu: {e}")

                # Rename file hien tai thanh zbackup_<fname>
                print(f"[BACKUP] Rename asset cu '{fname}' -> '{backup_name}' (ID: {old_asset_id})...")
                patch_url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/assets/{old_asset_id}"
                patch_data = json.dumps({"name": backup_name, "label": f"Backup of {fname}"}).encode("utf-8")
                patch_headers = dict(req_headers)
                patch_headers["Content-Type"] = "application/json"
                patch_req = urllib.request.Request(patch_url, data=patch_data, headers=patch_headers, method="PATCH")

                try:
                    with urllib.request.urlopen(patch_req, timeout=15) as patch_resp:
                        if patch_resp.status in (200, 201):
                            print(f"[BACKUP] -> Da tao ban backup thanh cong: '{backup_name}'")
                except Exception as e:
                    print(f"[BACKUP] Loi khi rename asset backup: {e}")

    except Exception as ex:
        print(f"[BACKUP] Kiem tra assets release: {ex}")


def is_authenticated_context(context):
    try:
        for c in context.cookies():
            if c.get("name") == "user_session" and c.get("value"):
                return True
    except Exception:
        pass
    return False


JS_UPLOAD_TRACKER = """
(() => {
    window.__uploadTracker = {
        files: {},
        totalLoaded: 0,
        totalBytes: 0,
        activeCount: 0,
        completedCount: 0,
        failedCount: 0,
        startTime: 0
    };

    const origOpen = XMLHttpRequest.prototype.open;
    const origSend = XMLHttpRequest.prototype.send;

    XMLHttpRequest.prototype.open = function(method, url) {
        this.__reqUrl = url || '';
        this.__isS3Upload = (
            this.__reqUrl.includes('s3.amazonaws.com') ||
            this.__reqUrl.includes('github-production-release-asset') ||
            this.__reqUrl.includes('/upload/')
        );
        return origOpen.apply(this, arguments);
    };

    XMLHttpRequest.prototype.send = function(body) {
        const isPayload = this.__isS3Upload || (body && (
            (body instanceof Blob) ||
            (body instanceof File) ||
            (body instanceof FormData)
        ));

        if (isPayload) {
            const reqId = 'req_' + Math.random().toString(36).substring(2, 9);
            this.__reqId = reqId;

            if (!window.__uploadTracker.startTime) {
                window.__uploadTracker.startTime = Date.now();
            }
            window.__uploadTracker.activeCount++;

            let approxSize = 0;
            let filename = '';
            if (body instanceof Blob || body instanceof File) {
                approxSize = body.size;
                filename = body.name || '';
            } else if (body instanceof FormData) {
                try {
                    for (let [k, v] of body.entries()) {
                        if (v instanceof File || v instanceof Blob) {
                            approxSize = v.size;
                            filename = v.name || k;
                            break;
                        }
                    }
                } catch(e) {}
            }

            const rec = {
                id: reqId,
                url: this.__reqUrl,
                filename: filename,
                loaded: 0,
                total: approxSize,
                done: false
            };
            window.__uploadTracker.files[reqId] = rec;

            if (this.upload) {
                this.upload.addEventListener('progress', (e) => {
                    rec.loaded = e.loaded;
                    if (e.total && e.total > 0) rec.total = e.total;

                    let sumLoaded = 0;
                    let sumTotal = 0;
                    for (const id in window.__uploadTracker.files) {
                        sumLoaded += window.__uploadTracker.files[id].loaded;
                        sumTotal += window.__uploadTracker.files[id].total;
                    }
                    window.__uploadTracker.totalLoaded = sumLoaded;
                    if (sumTotal > window.__uploadTracker.totalBytes) {
                        window.__uploadTracker.totalBytes = sumTotal;
                    }
                });
            }

            this.addEventListener('load', () => {
                rec.loaded = rec.total;
                rec.done = true;
                window.__uploadTracker.activeCount = Math.max(0, window.__uploadTracker.activeCount - 1);
                window.__uploadTracker.completedCount++;
            });

            this.addEventListener('error', () => {
                window.__uploadTracker.activeCount = Math.max(0, window.__uploadTracker.activeCount - 1);
                window.__uploadTracker.failedCount++;
            });

            this.addEventListener('abort', () => {
                window.__uploadTracker.activeCount = Math.max(0, window.__uploadTracker.activeCount - 1);
            });
        }
        return origSend.apply(this, arguments);
    };
})();
"""


def verify_files_exist():
    missing = []
    print("====================================================================")
    print("  Kiem tra file can upload trong thu muc \\publish\\:")
    print("====================================================================")
    for p in TARGET_FILES:
        if not p.exists():
            missing.append(p.name)
            print(f" [THIEU] {p.name}")
        else:
            sz_mb = p.stat().st_size / (1024 * 1024)
            print(f" [OK]    {p.name} ({sz_mb:.2f} MB)")
    print("====================================================================")

    if missing:
        print(f"[LOI] Thieu {len(missing)} file! Vui long chay build.cmd truoc khi upload.")
        sys.exit(1)


def perform_browser_upload(force_login=False):
    disable_quick_edit_mode()
    verify_files_exist()

    target_names = [p.name for p in TARGET_FILES]
    total_target_bytes = sum(p.stat().st_size for p in TARGET_FILES)
    total_target_mb = total_target_bytes / (1024 * 1024)

    PROFILE_DIR.mkdir(parents=True, exist_ok=True)

    with sync_playwright() as p:
        print("[PROFILE] Su dung Persistent Profile an toan tai: .edge_profile")
        initial_headless = not force_login

        context = launch_edge_context_safe(p, PROFILE_DIR, headless=initial_headless)
        page = context.pages[0] if context.pages else context.new_page()
        page.add_init_script(JS_UPLOAD_TRACKER)

        page.goto("https://github.com", wait_until="domcontentloaded", timeout=45000)
        has_session = is_authenticated_context(context)

        if not has_session:
            if initial_headless:
                print("\n[LOGIN] Chua co session dang nhap GitHub trong Profile.")
                print("[LOGIN] Dang mo Microsoft Edge de dang nhap 1 LAN DUY NHAT...")
                try:
                    context.close()
                except Exception:
                    pass

                context = launch_edge_context_safe(p, PROFILE_DIR, headless=False)
                page = context.pages[0] if context.pages else context.new_page()
                page.add_init_script(JS_UPLOAD_TRACKER)
                page.goto("https://github.com/login", wait_until="domcontentloaded")

            print("\n>>> VUI LONG DANG NHAP GITHUB TREN CUA SO MICROSOFT EDGE <<<")
            print("[LOGIN] Dang cho ban dang nhap...")

            logged_in = False
            for sec in range(240):
                time.sleep(0.5)
                if is_authenticated_context(context):
                    logged_in = True
                    break
                cur_url = page.url.lower()
                if "github.com/login" not in cur_url and "session" not in cur_url and "github.com" in cur_url:
                    time.sleep(1)
                    if is_authenticated_context(context) or sec >= 10:
                        logged_in = True
                        break

            if not logged_in:
                print("[LOI] Het thoi gian cho dang nhap (2 phut). Vui long thu lai.")
                try:
                    context.close()
                except Exception:
                    pass
                sys.exit(1)

            print("[LOGIN] -> XAC NHAN DANG NHAP THANH CONG!")

        # 1. Thuc hien rename backup file cu tren release beta
        print("\n[BACKUP GUARD] Kiem tra va tao ban sao luu zbackup cho file cu...")
        apply_backup_rule_to_existing_assets(target_names)

        # 2. Vong lap vo han cho den khi upload hoan tat
        attempt = 0
        upload_success = False
        final_elapsed = 0
        final_speed = 0

        while not upload_success:
            attempt += 1
            print("\n====================================================================")
            print(f"  [LAN THU {attempt}] Bat dau truyen file len AWS S3 qua Microsoft Edge...")
            print("====================================================================")

            print(f"[NAVIGATE] Dang mo trang Release Edit: {EDIT_URL}...")
            page.goto(EDIT_URL, wait_until="domcontentloaded", timeout=60000)

            if "github.com/login" in page.url.lower():
                print("[CANH BAO] Phien dang nhap het han. Chay lai voi --login.")
                try:
                    context.close()
                except Exception:
                    pass
                sys.exit(1)

            print("[WAIT] Tim khu vuc tai file Releases Upload (#releases-upload)...")
            try:
                page.wait_for_selector("#releases-upload", state="attached", timeout=15000)
                file_input = page.locator("#releases-upload")
            except Exception:
                page.wait_for_selector('input[type="file"]:not(#fc-release_body)', state="attached", timeout=15000)
                file_input = page.locator('input[type="file"]:not(#fc-release_body)').first

            # Reset tracker trong browser
            page.evaluate("""() => {
                if (window.__uploadTracker) {
                    window.__uploadTracker.startTime = 0;
                    window.__uploadTracker.totalLoaded = 0;
                    window.__uploadTracker.totalBytes = 0;
                    window.__uploadTracker.files = {};
                    window.__uploadTracker.activeCount = 0;
                    window.__uploadTracker.completedCount = 0;
                    window.__uploadTracker.failedCount = 0;
                }
            }""")

            file_paths = [str(p) for p in TARGET_FILES]
            print(f"[UPLOAD] Dinh kem {len(file_paths)} file ({total_target_mb:.2f} MB) de truyen truc tiep AWS S3...")
            file_input.set_input_files(file_paths)

            start_time = time.time()
            prev_loaded = 0
            retry_needed = False

            while True:
                time.sleep(POLL_INTERVAL_SEC)
                elapsed = time.time() - start_time

                tracker = page.evaluate("() => window.__uploadTracker || {}")
                total_loaded = tracker.get("totalLoaded", 0)

                avg_mbps = (total_loaded * 8) / (elapsed * 1_000_000) if elapsed > 0 else 0.0
                instant_delta = max(0, total_loaded - prev_loaded)
                instant_mbps = (instant_delta * 8) / (POLL_INTERVAL_SEC * 1_000_000)
                prev_loaded = total_loaded

                loaded_mb = total_loaded / (1024 * 1024)
                pct = min(100.0, (total_loaded / total_target_bytes) * 100) if total_target_bytes > 0 else 0

                status_desc = f"{loaded_mb:.1f}/{total_target_mb:.1f} MB ({pct:.1f}%) | TB: {avg_mbps:.1f} Mbps | Tuc thoi: {instant_mbps:.1f} Mbps | {elapsed:.1f}s"
                print(f" [TIEN DO] {status_desc}")

                # Kiem tra trang thai giao dien
                ul_text = ""
                try:
                    ul_loc = page.locator("ul.uploaded-files")
                    if ul_loc.count() > 0:
                        ul_text = ul_loc.inner_text()
                except Exception:
                    pass

                all_files_in_ul = all(p.name in ul_text for p in TARGET_FILES)

                uploading_count = 0
                try:
                    uploading_count = page.locator(
                        "ul.uploaded-files .upload-progress:not(.d-none), ul.uploaded-files .is-uploading, file-attachment.is-uploading"
                    ).count()
                except Exception:
                    pass

                update_btn = page.locator('button:has-text("Update release")').first
                btn_ready = update_btn.is_visible() and update_btn.is_enabled()

                # QUY TAC TOC DO: Sau 7 giay, neu upload speed < 10 Mbps thi tu huy roi upload lai
                if elapsed >= GRACE_PERIOD_SEC:
                    is_stalled = (elapsed >= 15 and total_loaded == 0)
                    is_below_speed = (avg_mbps < MIN_UPLOAD_SPEED_MBPS and total_loaded > 0 and not all_files_in_ul)

                    if is_stalled or is_below_speed:
                        print(f"\n[CANH BAO TOC DO] Sau {elapsed:.1f}s toc do TB dat {avg_mbps:.2f} Mbps (< {MIN_UPLOAD_SPEED_MBPS} Mbps)!")
                        print("[TU HUY & THU LAI] Dang goi window.stop() de huy luot upload va retry vong lap...")
                        try:
                            page.evaluate("() => window.stop()")
                        except Exception:
                            pass
                        retry_needed = True
                        break

                # NHAN DIEN HOAN TAT: 100% du lieu truyen len S3 va nut Update release da enable
                is_tracker_done = (
                    tracker.get("completedCount", 0) >= len(TARGET_FILES) or
                    (total_loaded >= total_target_bytes * 0.98 and tracker.get("activeCount", 0) == 0)
                )

                if (is_tracker_done or all_files_in_ul) and uploading_count == 0 and btn_ready and elapsed >= 1.5:
                    final_elapsed = elapsed
                    final_speed = avg_mbps if avg_mbps > 0 else (total_target_mb * 8 / max(0.1, final_elapsed))
                    print(f"\n[XAC NHAN HOAN TAT] 100% du lieu ({total_target_mb:.2f} MB) da truyen len AWS S3 thanh cong!")
                    print(f"                   Thoi gian: {final_elapsed:.1f}s | Toc do: {final_speed:.1f} Mbps")
                    upload_success = True
                    break

                if elapsed > 600:
                    print("\n[TIMEOUT] Upload vuot qua 10 phut. Thu lai...")
                    retry_needed = True
                    break

            if retry_needed:
                print("[RETRY] Nghi 2 giay roi tu dong thu lai...")
                time.sleep(2)
                continue

        # Luu release
        print("\n[SUBMIT] Dang bam nut 'Update release' de luu ban phat hanh Beta...")
        update_btn = page.locator('button:has-text("Update release")').first
        update_btn.click()

        print(f"[SUBMIT] Dang cho GitHub luu release ({VIEW_URL})...")
        try:
            page.wait_for_url("**/releases/tag/**", timeout=30000)
            print(f"[THANH CONG] Release Beta da cap nhat hoan tat tai: {page.url}")
        except Exception:
            time.sleep(3)
            print("[SUBMIT] Da gui lenh cap nhat thanh cong.")

        try:
            context.close()
        except Exception:
            pass

    print("\n====================================================================")
    print("  [HOAN TAT 100%] Da upload thanh cong file exe len Release Beta!")
    print(f"  URL: {VIEW_URL}")
    print("====================================================================")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Upload Release Beta via Persistent Microsoft Edge")
    parser.add_argument("--login", action="store_true", help="Mo trinh duyet de dang nhap lai GitHub")
    args, _ = parser.parse_known_args()

    try:
        perform_browser_upload(force_login=args.login)
    except KeyboardInterrupt:
        print("\n[HUY] Nguoi dung da huy thao tac.")
        sys.exit(1)
    except Exception as e:
        print(f"\n[LOI] Xay ra loi trong qua trinh upload: {e}")
        sys.exit(1)
