using System;
using System.Runtime.InteropServices;

namespace MkLink.Services
{
    public static class AdminDragDropHelper
    {
        private const uint MSGFLT_ALLOW = 1;
        private const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_COPYGLOBALDATA = 0x0049;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint msg, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr changeFilterStruct);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        public static void EnableProcessWideFilter()
        {
            try
            {
                ChangeWindowMessageFilter(WM_DROPFILES, MSGFLT_ALLOW);
                ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ALLOW);
                ChangeWindowMessageFilter(WM_COPYGLOBALDATA, MSGFLT_ALLOW);
            }
            catch
            {
            }
        }

        public static void EnableWindowFilter(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                EnableProcessWideFilter();
                ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
                DragAcceptFiles(hwnd, true);
            }
            catch
            {
            }
        }
    }
}
