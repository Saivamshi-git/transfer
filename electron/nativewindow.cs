using System;
using System.Drawing; // Required for the Point struct
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopElementInspector
{
    /// <summary>
    /// Contains P/Invoke method signatures for interacting with the native Win32 API.
    /// </summary>
    internal static class NativeMethods
    {
        // --- Standard Window and P/Invoke Methods from your original file ---

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);


        // --- New methods and constants for the robust idle check ---

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
        
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(out CURSORINFO pci);
        
        public const uint WM_NULL = 0x0000;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        private static readonly IntPtr IDC_WAIT = new IntPtr(32514);       // Standard busy spinner
        private static readonly IntPtr IDC_APPSTARTING = new IntPtr(32650); // Busy spinner with a pointer


        // --- Structs ---

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public Point ptScreenPos;
        }

        // --- Helper Logic ---
        
        /// <summary>
        /// Checks if the global system cursor is currently a 'busy' cursor.
        /// </summary>
        public static bool IsSystemCursorBusy()
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (GetCursorInfo(out ci))
            {
                // CURSOR_SHOWING = 1
                if (ci.flags == 1 && (ci.hCursor == IDC_WAIT || ci.hCursor == IDC_APPSTARTING))
                {
                    return true;
                }
            }
            return false;
        }
    }
}