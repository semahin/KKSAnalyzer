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
Run("Номер в заголовках IA и ID не влияет на распознавание", () =>
{
    var document = KksParser.Parse("#IA900\nA_X1\n#\n#id1200\nB_X2\n#");
    Equal(2, document.Signals.Count);
    Equal(KksSection.Analog, document.Signals.Single(x => x.Code == "A_X1").Section);
    Equal(KksSection.Discrete, document.Signals.Single(x => x.Code == "B_X2").Section);
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
    Equal(2, result.AnalogSuffixes[0].Codes.Count);
    Equal("A_X1", result.AnalogSuffixes[0].Codes[0]);
    Equal("X2", result.DiscreteSuffixes[0].Suffix);
});
Run("Пересечение постфиксов IA и ID", () =>
{
    var document = KksParser.Parse("#IA1000\nA_X1\nB_X1\nC_X2\n#\n#ID1000\nD_x1\nE_X3\n#");
    var result = KksAnalyzerService.Analyze(document);
    Equal(1, result.CommonSuffixes.Count);
    Equal("X1", result.CommonSuffixes[0].Suffix);
    Equal(2, result.CommonSuffixes[0].AnalogCount);
    Equal(1, result.CommonSuffixes[0].DiscreteCount);
    Equal(2, result.CommonSuffixes[0].AnalogCodes.Count);
    Equal("D_x1", result.CommonSuffixes[0].DiscreteCodes[0]);
});
Run("Звёздочка в быстром поиске заменяет любую строку", () =>
{
    Equal(true, SearchPatternMatcher.Contains("20KKS_X1", "20*X1"));
    Equal(true, SearchPatternMatcher.Contains("prefix-20KKS_X1-suffix", "20*X1"));
    Equal(false, SearchPatternMatcher.Contains("20KKS_X2", "20*X1"));
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
Run("Поиск кода со звёздочкой в документах", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "KKSAnalyzerTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "source.txt");
    File.WriteAllText(path, "Сигналы 20KKS_X1 и 20ABC_X2.");
    try
    {
        var result = DocumentSearchService.Search(["20*X1"], [path]);
        Equal(1, result.Matches.Count);
        Equal(1, result.Matches[0].Count);
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
Run("Выгрузка общих сигналов по разделам эталонного файла", () =>
{
    var reference = KksParser.Parse("#IA900\nA_X1\nB_X2\n#\n#ID900\nC_X3\nD_X4\n#");
    var second = KksParser.Parse("#IA1200\nC_X3\nE_X5\n#\n#ID1200\nA_X1\nD_X4\n#");
    var content = KksAnalyzerService.ExportCommonByReferenceSections(reference, second);
    var result = KksParser.Parse(content);
    Equal(3, result.Signals.Count);
    Equal(KksSection.Analog, result.Signals.Single(x => x.Code == "A_X1").Section);
    Equal(KksSection.Discrete, result.Signals.Single(x => x.Code == "C_X3").Section);
    Equal(KksSection.Discrete, result.Signals.Single(x => x.Code == "D_X4").Section);
    Equal(false, result.Signals.Any(x => x.Code == "B_X2" || x.Code == "E_X5"));
    Equal(true, content.Contains("#IA900"));
    Equal(true, content.Contains("#ID900"));
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
Run("Поиск ошибок распределения IA и ID", () =>
{
    var reference = KksParser.Parse("#IA1000\nA_X1\nB_X2\n#\n#ID1000\nC_X3\n#");
    var checkedDocument = KksParser.Parse("#IA1000\nC_X3\nD_X4\n#\n#ID1000\nA_X1\n#");
    var result = KksAnalyzerService.CompareSections(reference, checkedDocument);
    Equal(2, result.CommonSignalCount);
    Equal(2, result.Mismatches.Count);
    Equal(KksSection.Analog, result.Mismatches.Single(x => x.Code == "A_X1").ExpectedSection);
    Equal(KksSection.Discrete, result.Mismatches.Single(x => x.Code == "C_X3").ExpectedSection);
});
Run("Перенос подтверждённых сигналов между IA и ID", () =>
{
    var document = KksParser.Parse("; comment\n#IA900\nC_X3\nD_X4\n#\n#ID900\nA_X1\nE_X5\n#");
    var correctedText = KksAnalyzerService.MoveSignalsToSections(document,
        [("A_X1", KksSection.Analog), ("C_X3", KksSection.Discrete)]);
    var corrected = KksParser.Parse(correctedText);
    Equal(KksSection.Analog, corrected.Signals.Single(x => x.Code == "A_X1").Section);
    Equal(KksSection.Discrete, corrected.Signals.Single(x => x.Code == "C_X3").Section);
    Equal(KksSection.Analog, corrected.Signals.Single(x => x.Code == "D_X4").Section);
    Equal(true, correctedText.Contains("; comment"));
    Equal(true, correctedText.Contains("#IA900"));
    Equal(true, correctedText.Contains("#ID900"));
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}
Console.WriteLine("Все проверки пройдены (18/18).");
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
