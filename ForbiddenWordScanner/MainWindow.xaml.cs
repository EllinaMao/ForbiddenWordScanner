using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Threading;

namespace ForbiddenWordScanner
{
    public partial class MainWindow : Window
    {
        private string selectedPath;
        private CancellationTokenSource cts;
        private ConcurrentBag<ResultItem> results = new ConcurrentBag<ResultItem>();
        private bool isPaused = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoadWords_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                WordsTextBox.Text = File.ReadAllText(dlg.FileName);
            }
        }

        private void SelectPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                CheckFileExists = false,
                FileName = "Выберите папку или файл"
            };

            if (dlg.ShowDialog() == true)
            {
                selectedPath = Path.GetDirectoryName(dlg.FileName);
                MessageBox.Show($"Выбрана папка: {selectedPath}", "Путь выбран");
            }
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(WordsTextBox.Text))
            {
                MessageBox.Show("Введите или загрузите запрещённые слова.");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                MessageBox.Show("Выберите папку для сканирования.");
                return;
            }

            cts = new CancellationTokenSource();
            isPaused = false;
            results = new ConcurrentBag<ResultItem>();
            ResultsGrid.ItemsSource = null;
            MainProgressBar.Value = 0;

            var words = WordsTextBox.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(w => w.Trim())
                                         .Where(w => !string.IsNullOrEmpty(w))
                                         .ToArray();

            ForbiddenWordsList.ItemsSource = words;

            await Task.Run(() => ScanDirectory(selectedPath, words, cts.Token));

            if (!cts.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    ResultsGrid.ItemsSource = results.OrderByDescending(r => r.Replacements).ToList();
                    SaveReport();
                    MessageBox.Show("Сканирование завершено. Отчёт создан.");
                });
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            isPaused = true;
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            isPaused = false;
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            cts?.Cancel();
            MessageBox.Show("Сканирование остановлено.");
        }

        private void ScanDirectory(string path, string[] words, CancellationToken token)
        {
            try
            {
                var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                                     .Where(f => f.EndsWith(".txt") || f.EndsWith(".log") || f.EndsWith(".cs"))
                                     .ToList();

                int totalFiles = files.Count;
                int processed = 0;

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) break;

                    while (isPaused)
                    {
                        Thread.Sleep(200);
                        if (token.IsCancellationRequested) return;
                    }

                    try
                    {
                        string content = File.ReadAllText(file);
                        int totalReplacements = 0;

                        foreach (var word in words)
                        {
                            int count = content.Split(new[] { word }, StringSplitOptions.None).Length - 1;
                            if (count > 0)
                            {
                                content = content.Replace(word, new string('*', word.Length));
                                totalReplacements += count;
                            }
                        }

                        if (totalReplacements > 0)
                        {
                            string destDir = Path.Combine(Environment.CurrentDirectory, "FilteredFiles");
                            Directory.CreateDirectory(destDir);
                            string destFile = Path.Combine(destDir, Path.GetFileName(file));
                            File.WriteAllText(destFile, content);

                            results.Add(new ResultItem
                            {
                                FilePath = file,
                                Size = new FileInfo(file).Length,
                                Replacements = totalReplacements
                            });
                        }
                    }
                    catch { }

                    processed++;
                    double progress = (double)processed / totalFiles * 100;
                    Dispatcher.Invoke(() => MainProgressBar.Value = progress, DispatcherPriority.Background);
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Ошибка: {ex.Message}"));
            }
        }

        private void SaveReport()
        {
            string reportPath = Path.Combine(Environment.CurrentDirectory, "Report.txt");
            using StreamWriter sw = new StreamWriter(reportPath);

            sw.WriteLine("Отчёт сканирования запрещённых слов:\n");

            foreach (var r in results.OrderByDescending(r => r.Replacements))
            {
                sw.WriteLine($"{r.FilePath} | {r.Size} байт | {r.Replacements} замен");
            }

            if (results.Count == 0)
                sw.WriteLine("Запрещённые слова не найдены.");

            sw.WriteLine("\nСканирование завершено в " + DateTime.Now);
        }
    }

    public class ResultItem
    {
        public string FilePath { get; set; }
        public long Size { get; set; }
        public int Replacements { get; set; }
    }
}
