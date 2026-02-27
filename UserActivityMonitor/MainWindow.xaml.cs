using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Ookii.Dialogs.Wpf;
using System.Runtime.InteropServices;

namespace UserMonitor
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _monitorTimer;
        private StringBuilder _typedText = new();
        private HashSet<string> _observedProcesses = new();
        private bool _isMonitoring = false;

        private HashSet<string> _forbiddenWords = new();
        private HashSet<string> _forbiddenApps = new();
        private string _reportPath;

        private DateTime _monitorStartTime;
        private int _monitorSeconds = 30;

        private readonly Dictionary<char, char> enToRu = new()
        {
            {'q','й'}, {'w','ц'}, {'e','у'}, {'r','к'}, {'t','е'}, {'y','н'}, {'u','г'}, {'i','ш'}, {'o','щ'}, {'p','з'},
            {'[','х'}, {']','ъ'}, {'a','ф'}, {'s','ы'}, {'d','в'}, {'f','а'}, {'g','п'}, {'h','р'}, {'j','о'}, {'k','л'},
            {'l','д'}, {';','ж'}, {'\'','э'}, {'z','я'}, {'x','ч'}, {'c','с'}, {'v','м'}, {'b','и'}, {'n','т'}, {'m','ь'},
            {',','б'}, {'.','ю'}
        };
        private readonly Dictionary<char, char> ruToEn;

        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint idThread);

        public MainWindow()
        {
            InitializeComponent();

            ruToEn = enToRu.ToDictionary(kv => kv.Value, kv => kv.Key);

            _monitorTimer = new DispatcherTimer();
            _monitorTimer.Interval = TimeSpan.FromMilliseconds(100);
            _monitorTimer.Tick += MonitorTimer_Tick;

            _reportPath = AppDomain.CurrentDomain.BaseDirectory;
            ReportPathBox.Text = _reportPath;
        }

        private void LoadForbiddenWords_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _forbiddenWords = new HashSet<string>(File.ReadAllLines(dlg.FileName).Select(l => l.Trim().ToLower()));
                ForbiddenWordsBox.Text = string.Join(", ", _forbiddenWords);
            }
        }

        private void LoadForbiddenApps_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _forbiddenApps = new HashSet<string>(File.ReadAllLines(dlg.FileName).Select(l => l.Trim().ToLower()));
                ForbiddenAppsBox.Text = string.Join(", ", _forbiddenApps);
            }
        }

        private void SelectReportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog { Description = "Выберите папку для отчётов", SelectedPath = _reportPath };
            if (dlg.ShowDialog(this) == true)
            {
                _reportPath = dlg.SelectedPath;
                ReportPathBox.Text = _reportPath;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Настройки сохранены");
        }

        private void StartMonitoring_Click(object sender, RoutedEventArgs e)
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _typedText.Clear();
            _observedProcesses.Clear();
            MonitoringProgressBar.Value = 0;

            _monitorStartTime = DateTime.Now;

            CloseForbiddenAppsImmediately();

            RefreshKeyboardDataGrid();
            RefreshProcessDataGrid();

            _monitorTimer.Start();
        }

        private void StopMonitoring_Click(object sender, RoutedEventArgs e)
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            _monitorTimer.Stop();
            MonitoringProgressBar.Value = 0;

            MessageBox.Show("Мониторинг остановлен");
        }

        private void MainWindow_TextInput(object sender, TextCompositionEventArgs e)
        {
            if (KeyboardCheckBox.IsChecked != true) return;

            string text = e.Text;
            _typedText.Append(text);

            string layout = GetCurrentKeyboardLayout();
            string allKeysPath = Path.Combine(_reportPath, "all_keys.txt");
            File.AppendAllText(allKeysPath, $"{DateTime.Now} [{layout}]: {text}{Environment.NewLine}");

            if (ModerationCheckBox.IsChecked == true)
            {
                string textLower = _typedText.ToString().ToLower();
                foreach (var word in _forbiddenWords)
                {
                    if (textLower.Contains(word))
                    {
                        string forbiddenPath = Path.Combine(_reportPath, "forbidden_keys.txt");
                        File.AppendAllText(forbiddenPath, $"{DateTime.Now}: Слово '{word}' набрано{Environment.NewLine}");
                        _typedText.Clear();
                    }
                }
            }

            RefreshKeyboardDataGrid();
        }

        private string GetCurrentKeyboardLayout()
        {
            IntPtr hLayout = GetKeyboardLayout(0);
            int lid = hLayout.ToInt32() & 0xFFFF;
            return lid switch
            {
                0x419 => "RU",
                0x409 => "EN",
                _ => "OTHER"
            };
        }

        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.Now - _monitorStartTime).TotalSeconds;
            double progress = Math.Min(elapsed / _monitorSeconds * 100, 100);
            MonitoringProgressBar.Value = progress;

            if (progress >= 100)
            {
                StopMonitoring_Click(null, null);
                MessageBox.Show("Мониторинг завершен");
                return;
            }

            if (ProcessCheckBox.IsChecked == true)
            {
                foreach (var p in Process.GetProcesses())
                {
                    string procName = p.ProcessName.ToLower();
                    if (!_observedProcesses.Contains(procName))
                    {
                        _observedProcesses.Add(procName);

                        string filePath = Path.Combine(_reportPath, "process_report.txt");
                        File.AppendAllText(filePath, $"{DateTime.Now}: {p.ProcessName} запущен{Environment.NewLine}");

                        if (ModerationCheckBox.IsChecked == true && _forbiddenApps.Contains(procName))
                        {
                            try { p.Kill(); } catch { }
                        }

                        RefreshProcessDataGrid();
                    }
                }
            }

            RefreshKeyboardDataGrid();
        }

        private void RefreshKeyboardDataGrid()
        {
            string filePath = Path.Combine(_reportPath, "all_keys.txt");
            if (File.Exists(filePath))
                KeyboardDataGrid.ItemsSource = File.ReadAllLines(filePath).Select(l => new { Info = l });
            else
                KeyboardDataGrid.ItemsSource = null;
        }

        private void RefreshProcessDataGrid()
        {
            string filePath = Path.Combine(_reportPath, "process_report.txt");
            if (File.Exists(filePath))
                ProcessDataGrid.ItemsSource = File.ReadAllLines(filePath).Select(l => new { Info = l });
            else
                ProcessDataGrid.ItemsSource = null;
        }

        private void CloseForbiddenAppsImmediately()
        {
            if (_forbiddenApps.Count == 0) return;

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string procName = p.ProcessName.ToLower();
                    if (_forbiddenApps.Contains(procName))
                        p.Kill();
                }
                catch { }
            }
        }

        private string ConvertLayout(string input, bool toRussian)
        {
            var dict = toRussian ? enToRu : ruToEn;
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (dict.ContainsKey(c))
                    sb.Append(dict[c]);
                else if (dict.ContainsKey(char.ToLower(c)))
                    sb.Append(char.IsUpper(c) ? char.ToUpper(dict[char.ToLower(c)]) : dict[char.ToLower(c)]);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private void ConvertLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (KeyboardDataGrid.ItemsSource == null) return;

            var lines = KeyboardDataGrid.ItemsSource.Cast<dynamic>().Select(x => x.Info).ToList();
            var converted = lines.Select(l => ConvertLayout(l, true)).ToList();

            KeyboardDataGrid.ItemsSource = converted.Select(l => new { Info = l });
        }
    }
}
