using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using KKSAnalyzer.Core;
using Microsoft.Win32;

namespace KKSAnalyzer.App;

public partial class MainWindow : Window
{
    private KksDocument? _singleDocument;
    private string? _singlePath;
    private KksDocument? _firstDocument;
    private KksDocument? _secondDocument;
    private ComparisonResult? _comparison;
    private readonly List<SearchFileItem> _searchFiles = [];

    public MainWindow() => InitializeComponent();

    private static string? ChooseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл конфигурации",
            Filter = "Файлы конфигурации (*.txt;*.cfg)|*.txt;*.cfg|Все файлы (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void OpenSingleFile_Click(object sender, RoutedEventArgs e)
    {
        var path = ChooseFile();
        if (path is null) return;
        try { DisplaySingleFile(path); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void DisplaySingleFile(string path)
    {
        _singleDocument = KksParser.Load(path);
        _singlePath = path;
        var analysis = KksAnalyzerService.Analyze(_singleDocument);
        AllSignalsGrid.ItemsSource = analysis.Document.Signals.Select(x => new SignalRow(
            x.Code,
            x.MainPart,
            x.Suffix ?? "—",
            KksAnalyzerService.SectionName(x.Section),
            x.LineNumber)).ToList();
            DuplicatesGrid.ItemsSource = analysis.Duplicates.Select(x => new
            {
                x.Code,
                SectionDisplay = KksAnalyzerService.SectionName(x.Section),
                x.Count,
                x.Lines
            });
            NoSuffixGrid.ItemsSource = analysis.WithoutSuffix.Select(x => new
            {
                x.Code,
                SectionDisplay = KksAnalyzerService.SectionName(x.Section),
                x.LineNumber
            });
            AnalogSuffixGrid.ItemsSource = analysis.AnalogSuffixes;
            DiscreteSuffixGrid.ItemsSource = analysis.DiscreteSuffixes;
            SingleFileName.Text = path;
            SingleFileName.ToolTip = path;
            SingleSummary.Text = $"Сигналов: {analysis.Document.Signals.Count}  •  групп дубликатов: {analysis.Duplicates.Count}  •  без постфикса: {analysis.WithoutSuffix.Count}  •  постфиксов IA: {analysis.AnalogSuffixes.Count}  •  постфиксов ID: {analysis.DiscreteSuffixes.Count}";
            SaveCleanButton.IsEnabled = analysis.Duplicates.Count > 0;
        AllSignalsGrid.UnselectAll();
        UpdateSignalSelectionSummary();
    }

    private void SaveCleanFile_Click(object sender, RoutedEventArgs e)
    {
        if (_singleDocument is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить очищенный файл",
            Filter = "CFG (*.cfg)|*.cfg|TXT (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = "config_without_duplicates.cfg"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, KksAnalyzerService.RemoveDuplicates(_singleDocument), new System.Text.UTF8Encoding(false));
            MessageBox.Show(this, "Очищенный файл сохранён. Исходный файл не изменён.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void AllSignalsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSignalSelectionSummary();

    private void SelectAllSignals_Click(object sender, RoutedEventArgs e)
    {
        AllSignalsGrid.SelectAll();
        AllSignalsGrid.Focus();
    }

    private void ClearSignalSelection_Click(object sender, RoutedEventArgs e) => AllSignalsGrid.UnselectAll();

    private void UpdateSignalSelectionSummary()
    {
        if (SignalSelectionSummary is null || ExcludeSelectedButton is null || AllSignalsGrid is null) return;
        var count = AllSignalsGrid.SelectedItems.Count;
        SignalSelectionSummary.Text = count == 0 ? "Ничего не выбрано" : $"Выбрано сигналов: {count}";
        ExcludeSelectedButton.IsEnabled = count > 0 && _singleDocument is not null;
    }

    private void ExcludeSelectedSignals_Click(object sender, RoutedEventArgs e)
    {
        if (_singleDocument is null) return;
        var codes = AllSignalsGrid.SelectedItems.Cast<SignalRow>()
            .Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0) return;

        var extension = Path.GetExtension(_singlePath);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".cfg";
        var baseName = Path.GetFileNameWithoutExtension(_singlePath) ?? "config";
        var dialog = new SaveFileDialog
        {
            Title = $"Исключить выбранные сигналы ({codes.Count})",
            Filter = "CFG (*.cfg)|*.cfg|TXT (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = $"{baseName}_without_selected{extension}"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var content = KksAnalyzerService.RemoveCodes(_singleDocument, codes);
            File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(false));
            DisplaySingleFile(dialog.FileName);
            MessageBox.Show(this,
                $"Исключено кодов: {codes.Count}. Новая копия сохранена и открыта для дальнейшей работы.",
                "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OpenFirstFile_Click(object sender, RoutedEventArgs e) => LoadComparisonFile(true);
    private void OpenSecondFile_Click(object sender, RoutedEventArgs e) => LoadComparisonFile(false);

    private void LoadComparisonFile(bool first)
    {
        var path = ChooseFile();
        if (path is null) return;
        try
        {
            var document = KksParser.Load(path);
            if (first)
            {
                _firstDocument = document;
                FirstFileName.Text = path;
                FirstFileName.ToolTip = path;
            }
            else
            {
                _secondDocument = document;
                SecondFileName.Text = path;
                SecondFileName.ToolTip = path;
            }
            UpdateComparison();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void UpdateComparison()
    {
        if (_firstDocument is null || _secondDocument is null) return;
        _comparison = KksAnalyzerService.Compare(_firstDocument, _secondDocument);
        ComparisonSummary.Text = $"Общие (дубликаты между файлами): {_comparison.CommonCount}  •  только файл 1: {_comparison.OnlyFirstCount}  •  только файл 2: {_comparison.OnlySecondCount}";
        RunComparisonActionButton.IsEnabled = true;
        ApplyComparisonFilter();
    }

    private void ComparisonFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyComparisonFilter();

    private void ApplyComparisonFilter()
    {
        if (_comparison is null || ComparisonGrid is null || ComparisonFilter.SelectedItem is not ComboBoxItem selected) return;
        var filter = selected.Content?.ToString();
        ComparisonGrid.ItemsSource = filter == "Все сигналы"
            ? _comparison.Rows
            : _comparison.Rows.Where(x => x.Location == filter).ToList();
    }

    private void ComparisonAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComparisonActionHint is null || ComparisonAction.SelectedItem is not ComboBoxItem item) return;
        ComparisonActionHint.Text = item.Tag?.ToString() switch
        {
            "first_minus_common" => "Удалит из копии файла 1 все коды, которые встречаются в файле 2.",
            "second_minus_common" => "Удалит из копии файла 2 все коды, которые встречаются в файле 1.",
            "merge_into_first" => "Создаст конфигурацию из всех уникальных кодов; при совпадении раздел берётся из файла 1.",
            "merge_into_second" => "Создаст конфигурацию из всех уникальных кодов; при совпадении раздел берётся из файла 2.",
            "union" => "Выгрузит полный набор уникальных кодов из обоих файлов.",
            "intersection" => "Выгрузит только коды, присутствующие одновременно в двух файлах.",
            "symmetric_difference" => "Выгрузит коды, которые присутствуют только в одном из двух файлов.",
            _ => string.Empty
        };
    }

    private void RunComparisonAction_Click(object sender, RoutedEventArgs e)
    {
        if (_firstDocument is null || _secondDocument is null || _comparison is null ||
            ComparisonAction.SelectedItem is not ComboBoxItem item) return;

        var commonCodes = _comparison.Rows.Where(x => x.Location == "В обоих файлах").Select(x => x.Code);
        string content;
        string defaultName;
        switch (item.Tag?.ToString())
        {
            case "first_minus_common":
                content = KksAnalyzerService.RemoveCodes(_firstDocument, commonCodes);
                defaultName = "file1_without_common.cfg";
                break;
            case "second_minus_common":
                content = KksAnalyzerService.RemoveCodes(_secondDocument, commonCodes);
                defaultName = "file2_without_common.cfg";
                break;
            case "merge_into_first":
                content = KksAnalyzerService.Merge(_firstDocument, _secondDocument);
                defaultName = "file1_extended.cfg";
                break;
            case "merge_into_second":
                content = KksAnalyzerService.Merge(_secondDocument, _firstDocument);
                defaultName = "file2_extended.cfg";
                break;
            case "intersection":
                content = KksAnalyzerService.ExportIntersection(_firstDocument, _secondDocument);
                defaultName = "common_codes.cfg";
                break;
            case "symmetric_difference":
                content = KksAnalyzerService.ExportSymmetricDifference(_firstDocument, _secondDocument);
                defaultName = "different_codes.cfg";
                break;
            default:
                content = KksAnalyzerService.ExportUnion(_firstDocument, _secondDocument);
                defaultName = "all_unique_codes.cfg";
                break;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить результат операции",
            Filter = "CFG (*.cfg)|*.cfg|TXT (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = defaultName
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(false));
            MessageBox.Show(this, "Результат сохранён в новый файл. Исходные файлы не изменены.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void CodeGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: { } item }) return;
        var code = item.GetType().GetProperty("Code")?.GetValue(item)?.ToString();
        if (string.IsNullOrWhiteSpace(code)) return;

        var codes = ParseCodes(CodeSearchBox.Text).ToList();
        if (!codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            codes.Add(code);
        CodeSearchBox.Text = string.Join(Environment.NewLine, codes);
        ModeTabs.SelectedIndex = 2;
        CodeSearchBox.Focus();
        CodeSearchBox.CaretIndex = CodeSearchBox.Text.Length;
    }

    private void AddSearchFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите документы для поиска KKS-кодов",
            Filter = DocumentSearchService.FileDialogFilter,
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var path in dialog.FileNames)
        {
            if (_searchFiles.All(x => !x.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                _searchFiles.Add(new SearchFileItem(path));
        }
        RefreshSearchFiles();
    }

    private void ClearSearchFiles_Click(object sender, RoutedEventArgs e)
    {
        _searchFiles.Clear();
        RefreshSearchFiles();
        CodeSearchResultsGrid.ItemsSource = null;
        CodeSearchSummary.Text = "Список документов очищен.";
    }

    private async void StartCodeSearch_Click(object sender, RoutedEventArgs e)
    {
        var codes = ParseCodes(CodeSearchBox.Text).ToList();
        if (codes.Count == 0)
        {
            MessageBox.Show(this, "Введите хотя бы один KKS-код.", "Нет кодов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_searchFiles.Count == 0)
        {
            MessageBox.Show(this, "Добавьте хотя бы один документ для поиска.", "Нет документов", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StartSearchButton.IsEnabled = false;
        CodeSearchSummary.Text = $"Идёт поиск: кодов {codes.Count}, документов {_searchFiles.Count}…";
        try
        {
            var paths = _searchFiles.Select(x => x.FullPath).ToList();
            var result = await Task.Run(() => DocumentSearchService.Search(codes, paths));
            CodeSearchResultsGrid.ItemsSource = result.Matches;
            var foundCodes = result.Matches.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var missing = codes.Count - foundCodes;
            CodeSearchSummary.Text = $"Совпадений: {result.Matches.Sum(x => x.Count)}  •  найдено кодов: {foundCodes} из {codes.Count}  •  не найдено: {missing}" +
                                     (result.Errors.Count > 0 ? $"  •  ошибок чтения: {result.Errors.Count}" : "");
            if (result.Errors.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join(Environment.NewLine, result.Errors.Select(x => $"{x.FileName}: {x.Message}")),
                    "Некоторые файлы не прочитаны", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { StartSearchButton.IsEnabled = true; }
    }

    private void SearchResult_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CodeSearchResultsGrid.SelectedItem is not CodeSearchResult result) return;
        try
        {
            Process.Start(new ProcessStartInfo(result.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static IEnumerable<string> ParseCodes(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private void RefreshSearchFiles()
    {
        SearchFilesList.ItemsSource = null;
        SearchFilesList.ItemsSource = _searchFiles;
        CodeSearchSummary.Text = $"Документов выбрано: {_searchFiles.Count}.";
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

    private sealed record SearchFileItem(string FullPath)
    {
        public string DisplayName => $"{Path.GetFileName(FullPath)}  —  {Path.GetDirectoryName(FullPath)}";
    }

    private sealed record SignalRow(string Code, string MainPart, string SuffixDisplay, string SectionDisplay, int LineNumber);
}
