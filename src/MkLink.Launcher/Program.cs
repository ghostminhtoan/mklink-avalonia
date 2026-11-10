using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MkLink.Launcher
{
    internal static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [STAThread]
        public static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string runtimesDir = Path.Combine(baseDir, ".portable", "runtimes");

            if (!Directory.Exists(runtimesDir))
            {
                string localAppDll = Path.Combine(baseDir, "MkLink.App.dll");
                if (File.Exists(localAppDll))
                {
                    runtimesDir = baseDir;
                }
                else
                {
                    MessageBox(IntPtr.Zero,
                        "Không tìm thấy thư mục thư viện runtimes:\n" + runtimesDir + "\n\nVui lòng chạy script Run-Release.ps1 để build vào thư mục publish đầy đủ.",
                        "MkLink-GMTPC Launcher Error", 0x10);
                    return;
                }
            }

            // 1. Đặt thư mục DLL native cho Windows
            try
            {
                SetDllDirectory(runtimesDir);
            }
            catch
            {
            }

            // 2. Hook resolver cho managed assemblies
            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                string candidate = Path.Combine(runtimesDir, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                {
                    try
                    {
                        return context.LoadFromAssemblyPath(candidate);
                    }
                    catch
                    {
                    }
                }
                return null;
            };

            // 3. Hook resolver cho unmanaged / native DLLs (SkiaSharp, etc.)
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, unmanagedDllName) =>
            {
                string nativeCandidate = Path.Combine(runtimesDir, unmanagedDllName);
                if (!nativeCandidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    nativeCandidate += ".dll";
                }

                if (File.Exists(nativeCandidate))
                {
                    try
                    {
                        return NativeLibrary.Load(nativeCandidate);
                    }
                    catch
                    {
                    }
                }

                string subNative = Path.Combine(runtimesDir, "runtimes", "win-x64", "native", unmanagedDllName);
                if (!subNative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    subNative += ".dll";
                }

                if (File.Exists(subNative))
                {
                    try
                    {
                        return NativeLibrary.Load(subNative);
                    }
                    catch
                    {
                    }
                }

                return IntPtr.Zero;
            };

            string mainAppDll = Path.Combine(runtimesDir, "MkLink.App.dll");
            if (!File.Exists(mainAppDll))
            {
                MessageBox(IntPtr.Zero,
                    "Không tìm thấy file thực thi chính:\n" + mainAppDll,
                    "MkLink-GMTPC Launcher Error", 0x10);
                return;
            }

            try
            {
                var appAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(mainAppDll);
                var programType = appAssembly.GetType("MkLink.Program");
                if (programType == null)
                {
                    MessageBox(IntPtr.Zero, "Không tìm thấy type MkLink.Program trong MkLink.App.dll", "Error", 0x10);
                    return;
                }

                var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                if (mainMethod == null)
                {
                    MessageBox(IntPtr.Zero, "Không tìm thấy hàm Main trong MkLink.Program", "Error", 0x10);
                    return;
                }

                mainMethod.Invoke(null, new object[] { args });
            }
            catch (Exception ex)
            {
                string message = ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString();
                MessageBox(IntPtr.Zero, "Lỗi khởi động ứng dụng:\n\n" + message, "MkLink-GMTPC Fatal Error", 0x10);
            }
        }
    }
}
