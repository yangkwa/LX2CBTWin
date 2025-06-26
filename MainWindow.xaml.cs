using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Configuration;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Drawing;

namespace LX2CBTWin
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // 윈도우 표시 모드 설정 (화면 캡처 방지)
        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        // 현재 포그라운드 윈도우 핸들 반환
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // 시스템 커서 변경 및 복구 관련 Win32 API
        [DllImport("user32.dll")]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);
        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
        [DllImport("user32.dll")]
        private static extern IntPtr CopyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, byte[] lpvBits);
        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

        // 키보드 후킹 및 입력 감지 관련 상수/필드
        private const uint WDA_NONE = 0;
        private const uint WDA_MONITOR = 1;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_SNAPSHOT = 0x2C; // PrintScreen
        private const int VK_CONTROL = 0x11;
        private const int VK_V = 0x56;
        private const int VK_C = 0x43;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_F4 = 0x73;
        private const int VK_TAB = 0x09;

        // 시스템 커서 ID 상수
        private const uint OCR_NORMAL = 32512;
        private const int IDC_ARROW = 32512;
        private IntPtr originalCursor = IntPtr.Zero; // 원래 커서 핸들
        private IntPtr transparentCursor = IntPtr.Zero; // 투명 커서 핸들

        // ICONINFO 구조체: 커서/아이콘 생성용
        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        // 키보드 입력 상태 확인용
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // 저수준 키보드 후킹 관련 필드/델리게이트
        private static IntPtr _hookID = IntPtr.Zero;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Win32 키보드 후킹 API
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // 마우스 위치 구조체
        [StructLayout(LayoutKind.Sequential)]
        public struct Win32Point
        {
            public int X;
            public int Y;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(ref Win32Point pt);

        // 윈도우 위치 구조체
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        // 주요 상태 변수
        bool alwaysOnTop = true;
        bool forceShutdown = false;
        private DispatcherTimer? foregroundCheckTimer;

        /// <summary>
        /// MainWindow 생성자: 각종 보안 기능 및 후킹, 커서 감시 타이머 초기화
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            mainTitle.Content = ConfigurationManager.AppSettings["Title"];
            if (ConfigurationManager.AppSettings["ShowCloseButton"] == "false")
                closeButton.Visibility = Visibility.Hidden;
            if (ConfigurationManager.AppSettings["Logo"] == "")
                logoImage.Visibility = Visibility.Hidden;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_MONITOR);

            // 원래 커서 저장 및 투명 커서 생성
            originalCursor = CopyIcon(LoadCursor(IntPtr.Zero, (int)OCR_NORMAL));
            transparentCursor = CreateTransparentCursor();

            // PrintScreen 감지 및 클립보드 삭제용 PreviewKeyDown 이벤트 등록
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // 저수준 키보드 후킹 등록 (PrintScreen, Ctrl+C/V 차단)
            _hookID = SetHook(_proc);

            // 프로그램 종료 시 커서/후킹 복구
            this.Closed += (s, e) =>
            {
                UnhookWindowsHookEx(_hookID);
                RestoreSystemCursor();
                ResetToDefaultSystemCursor();
            };
        }

        /// <summary>
        /// 투명 커서 생성 (32x32)
        /// </summary>
        private IntPtr CreateTransparentCursor()
        {
            byte[] ANDmask = new byte[32 * 4];
            IntPtr hMonoBitmap = CreateBitmap(32, 32, 1, 1, ANDmask);
            ICONINFO iconInfo = new ICONINFO();
            iconInfo.fIcon = false;
            iconInfo.xHotspot = 0;
            iconInfo.yHotspot = 0;
            iconInfo.hbmMask = hMonoBitmap;
            iconInfo.hbmColor = IntPtr.Zero;
            IntPtr hCursor = CreateIconIndirect(ref iconInfo);
            return hCursor;
        }

        /// <summary>
        /// 시스템 커서를 원래 커서로 복구
        /// </summary>
        private void RestoreSystemCursor()
        {
            if (originalCursor != IntPtr.Zero)
                SetSystemCursor(originalCursor, OCR_NORMAL);
        }

        /// <summary>
        /// 모든 주요 시스템 커서를 윈도우 기본값으로 복구
        /// </summary>
        private void ResetToDefaultSystemCursor()
        {
            int[] cursorTypes = new int[]
            {
                32512, // OCR_NORMAL (Arrow)
                32513, // OCR_IBEAM (Text)
                32514, // OCR_WAIT (Hourglass)
                32515, // OCR_CROSS (Cross)
                32516, // OCR_UP (Up Arrow)
                32517, // OCR_SIZE (Size All)
                32642, // OCR_HAND (Hand)
                32644, // OCR_APPSTARTING (Arrow + Hourglass)
                32645, // OCR_NO (No)
                32646, // OCR_HELP (Help)
                32640, // OCR_SIZEALL (Size All)
                32643, // OCR_SIZEWE (Size West-East)
                32644, // OCR_SIZENS (Size North-South)
                32645, // OCR_SIZENWSE (Size NorthWest-SouthEast)
                32646, // OCR_SIZENESW (Size NorthEast-SouthWest)
            };

            foreach (int cursorId in cursorTypes)
            {
                IntPtr hCursor = LoadCursor(IntPtr.Zero, cursorId);
                SetSystemCursor(hCursor, (uint)cursorId);
            }
        }

        /// <summary>
        /// PrintScreen 키 감지 시 클립보드 삭제 및 로그 출력
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.PrintScreen)
            {
                Debug.WriteLine("[DEBUG] PrintScreen key detected. Clipboard cleared.");
                Clipboard.Clear();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 저수준 키보드 후킹 등록 (PrintScreen, Ctrl+C/V 차단)
        /// </summary>
        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            {
                var curModule = curProcess.MainModule;
                if (curModule == null)
                    throw new InvalidOperationException("MainModule is null.");
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        /// <summary>
        /// 키보드 후킹 콜백: PrintScreen, Ctrl+C, Ctrl+V, Alt+F4, Alt+Tab 차단 및 클립보드 삭제
        /// </summary>
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                // PrintScreen 차단
                if (vkCode == VK_SNAPSHOT)
                {
                    Clipboard.Clear();
                    MessageBox.Show("시험 중 화면 캡쳐는 할 수 없습니다.");
                    Debug.WriteLine("[DEBUG] PrintScreen key detected (global hook). Clipboard cleared.");
                    return (IntPtr)1; // Block the key
                }
                // Ctrl+V(Paste) 차단
                if (vkCode == VK_V && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
                {
                    Clipboard.Clear();
                    MessageBox.Show("시험 중 붙여넣기는 할 수 없습니다.");
                    Debug.WriteLine("[DEBUG] Ctrl+V (Paste) detected (global hook). Clipboard cleared.");
                    return (IntPtr)1; // Block the key
                }
                // Ctrl+C(Copy) 차단
                if (vkCode == VK_C && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
                {
                    Clipboard.Clear();
                    MessageBox.Show("시험 중 복사는 할 수 없습니다.");
                    Debug.WriteLine("[DEBUG] Ctrl+C (Copy) detected (global hook). Clipboard cleared.");
                    return (IntPtr)1; // Block the key
                }                
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        /// <summary>
        /// WebView2 초기화 및 설정
        /// </summary>
        private async void webView_Loaded(object sender, RoutedEventArgs e)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userDataFolder = System.IO.Path.Combine(appData, "LX2CBT");

            var webView2Environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(webView2Environment);

            var url = ConfigurationManager.AppSettings["URL"];
            if (url != null)
                webView.Source = new Uri(url);

            webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }

        /// <summary>
        /// 윈도우 비활성화 시 항상 TopMost 유지
        /// </summary>
        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            Window window = (Window)sender;
            window.Topmost = alwaysOnTop;
        }

        /// <summary>
        /// 닫기 버튼 클릭 시 강제 종료
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            forceShutdown = true;
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Ctrl+Alt+X 단축키로 강제 종료
        /// </summary>
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.X && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    forceShutdown = true;
                    Application.Current.Shutdown();
                }
            }
        }

        /// <summary>
        /// 강제 종료가 아닌 경우 종료 차단
        /// </summary>
        private void MainWidow_Closing(object sender, CancelEventArgs e)
        {
            if (!forceShutdown)
                MessageBox.Show("CBT 프로그램은 종료할 수 없습니다. 관리자에게 문의하세요.");
            e.Cancel = true;
        }
    }
}
