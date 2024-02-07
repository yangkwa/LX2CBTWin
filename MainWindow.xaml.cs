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

namespace LX2CBTWin
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        bool alwaysOnTop = true;
        bool forceShutdown = false;
        public MainWindow()
        {
            InitializeComponent();

            mainTitle.Content = ConfigurationManager.AppSettings["Title"];
            if (ConfigurationManager.AppSettings["ShowCloseButton"] == "false")
                closeButton.Visibility = Visibility.Hidden;
            if (ConfigurationManager.AppSettings["Logo"] == "")
                logoImage.Visibility = Visibility.Hidden;
        }

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

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            Window window = (Window)sender;
            window.Topmost = alwaysOnTop;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            forceShutdown = true;
            Application.Current.Shutdown();
        }

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

        private void MainWidow_Closing(object sender, CancelEventArgs e)
        {
            if (!forceShutdown)
                MessageBox.Show("CBT 프로그램은 종료할 수 없습니다. 관리자에게 문의하세요.");
            e.Cancel = true;
        }
    }
}
