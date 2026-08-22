using KKSAnalyzer.Core;
using System.IO.Compression;

var failures = new List<string>();
Run("Парсинг секций и постфиксов", () =>
{
    var document = KksParser.Parse("#IA1000\n10BAC10CE011_XH43\n10BAC10CE012\n#\n#ID1000\n20ABC10AA001_XB01\n#");
    Equal(3, document.Signals.Count);
    Equal(KksSection.Analog, document.Signals[0].Section);
    Equal("XH43", document.Signals[0].Suffix);
    Equal(null, document.Signals[1].Suffix);
    Equal(KksSection.Discrete, document.Signals[2].Section);
});
Run("Дубликаты и удаление", () =>
{
    var document = KksParser.Parse("#IA1000\nA_X1\nA_X1\nB_X2\n#");
    var analysis = KksAnalyzerService.Analyze(document);
    Equal(1, analysis.Duplicates.Count);
    Equal(2, analysis.Duplicates[0].Count);
    Equal(1, KksParser.Parse(KksAnalyzerService.RemoveDuplicates(document)).Signals.Count(x => x.Code == "A_X1"));
});
Run("Сравнение двух файлов", () =>
{
    var first = KksParser.Parse("#IA1000\nA_X1\nB_X2\n#");
    var second = KksParser.Parse("#ID1000\na_x1\nC_X3\n#");
    var result = KksAnalyzerService.Compare(first, second);
    Equal(1, result.CommonCount);
    Equal(1, result.OnlyFirstCount);
    Equal(1, result.OnlySecondCount);
});
Run("Группировка постфиксов по разделам", () =>
{
    var document = KksParser.Parse("#IA1000\nA_X1\nB_X1\n#\n#ID1000\nC_X2\n#");
    var result = KksAnalyzerService.Analyze(document);
    Equal(2, result.AnalogSuffixes[0].Count);
    Equal("X2", result.DiscreteSuffixes[0].Suffix);
});
Run("Файл без системных строк", () =>
{
    var document = KksParser.Parse("10BAC10CE011_XH43\n10BAC10CE012\n\n10BAC10CE013_XB01");
    Equal(3, document.Signals.Count);
    Equal(KksSection.Unknown, document.Signals[0].Section);
    Equal("XH43", document.Signals[0].Suffix);
    Equal(null, document.Signals[1].Suffix);
});
Run("Пакетный поиск кодов в текстовых файлах", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "KKSAnalyzerTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "source.txt");
    File.WriteAllText(path, "Описание 10BAC10CE011_XH43 и повтор 10BAC10CE011_XH43.");
    try
    {
        var result = DocumentSearchService.Search(["10BAC10CE011_XH43", "NOT_FOUND"], [path]);
        Equal(1, result.Matches.Count);
        Equal(2, result.Matches[0].Count);
        Equal(0, result.Errors.Count);
    }
    finally { Directory.Delete(directory, true); }
});
Run("Поиск кода в DOCX с разными фрагментами форматирования", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "KKSAnalyzerTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "source.docx");
    using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()))
        writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>10BAC10</w:t></w:r><w:r><w:t>CE011_XH43</w:t></w:r></w:p></w:body></w:document>");
    try
    {
        var result = DocumentSearchService.Search(["10BAC10CE011_XH43"], [path]);
        Equal(1, result.Matches.Count);
    }
    finally { Directory.Delete(directory, true); }
});
Run("Удаление общих кодов из выбранного файла", () =>
{
    var document = KksParser.Parse("#IA1000\nA_X1\nB_X2\n#\n#ID1000\nC_X3\n#");
    var result = KksParser.Parse(KksAnalyzerService.RemoveCodes(document, ["B_X2", "C_X3"]));
    Equal(1, result.Signals.Count);
    Equal("A_X1", result.Signals[0].Code);
});
Run("Объединение сохраняет уникальные коды и разделы", () =>
{
    var first = KksParser.Parse("#IA1000\nA_X1\nB_X2\n#");
    var second = KksParser.Parse("#ID1000\nB_X2\nC_X3\n#");
    var result = KksParser.Parse(KksAnalyzerService.ExportUnion(first, second));
    Equal(3, result.Signals.Count);
    Equal(KksSection.Analog, result.Signals.Single(x => x.Code == "B_X2").Section);
    Equal(KksSection.Discrete, result.Signals.Single(x => x.Code == "C_X3").Section);
});
Run("Пересечение двух файлов", () =>
{
    var first = KksParser.Parse("A_X1\nB_X2");
    var second = KksParser.Parse("b_x2\nC_X3");
    var result = KksParser.Parse(KksAnalyzerService.ExportIntersection(first, second));
    Equal(1, result.Signals.Count);
    Equal("B_X2", result.Signals[0].Code);
});
Run("Симметрическая разница двух файлов", () =>
{
    var first = KksParser.Parse("A_X1\nB_X2");
    var second = KksParser.Parse("B_X2\nC_X3");
    var result = KksParser.Parse(KksAnalyzerService.ExportSymmetricDifference(first, second));
    Equal(2, result.Signals.Count);
    Equal(true, result.Signals.Any(x => x.Code == "A_X1"));
    Equal(true, result.Signals.Any(x => x.Code == "C_X3"));
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}
Console.WriteLine("Все проверки пройдены (11/11).");
return 0;

void Run(string name, Action test)
{
    try { test(); }
    catch (Exception ex) { failures.Add($"ОШИБКА: {name}: {ex.Message}"); }
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"ожидалось '{expected}', получено '{actual}'");
}
