namespace KKSAnalyzer.Core;

public static class KksAnalyzerService
{
    private static readonly StringComparer CodeComparer = StringComparer.OrdinalIgnoreCase;

    public static FileAnalysis Analyze(KksDocument document)
    {
        var duplicates = document.Signals
            .GroupBy(x => x.Code, CodeComparer)
            .Where(x => x.Count() > 1)
            .Select(x => new DuplicateGroup(
                x.Key,
                x.First().Section,
                x.Count(),
                string.Join(", ", x.Select(s => s.LineNumber))))
            .OrderBy(x => x.Code, CodeComparer)
            .ToList();

        return new FileAnalysis
        {
            Document = document,
            Duplicates = duplicates,
            WithoutSuffix = document.Signals.Where(x => x.Suffix is null).ToList(),
            AnalogSuffixes = BuildSuffixGroups(document.Signals, KksSection.Analog),
            DiscreteSuffixes = BuildSuffixGroups(document.Signals, KksSection.Discrete)
        };
    }

    public static ComparisonResult Compare(KksDocument first, KksDocument second)
    {
        var firstByCode = first.Signals.GroupBy(x => x.Code, CodeComparer)
            .ToDictionary(x => x.Key, x => x.First(), CodeComparer);
        var secondByCode = second.Signals.GroupBy(x => x.Code, CodeComparer)
            .ToDictionary(x => x.Key, x => x.First(), CodeComparer);
        var allCodes = firstByCode.Keys.Concat(secondByCode.Keys).Distinct(CodeComparer).OrderBy(x => x, CodeComparer);
        var rows = new List<ComparisonRow>();

        foreach (var code in allCodes)
        {
            var inFirst = firstByCode.TryGetValue(code, out var firstSignal);
            var inSecond = secondByCode.TryGetValue(code, out var secondSignal);
            var location = inFirst && inSecond ? "В обоих файлах" : inFirst ? "Только в файле 1" : "Только в файле 2";
            rows.Add(new ComparisonRow(
                code,
                location,
                inFirst ? SectionName(firstSignal!.Section) : "—",
                inSecond ? SectionName(secondSignal!.Section) : "—"));
        }

        return new ComparisonResult { Rows = rows };
    }

    public static string RemoveDuplicates(KksDocument document)
    {
        var duplicateLines = document.Signals
            .GroupBy(x => x.Code, CodeComparer)
            .SelectMany(group => group.Skip(1))
            .Select(x => x.LineNumber - 1)
            .ToHashSet();

        return string.Join(Environment.NewLine,
            document.Lines.Where((_, index) => !duplicateLines.Contains(index)));
    }

    public static string RemoveCodes(KksDocument document, IEnumerable<string> codes)
    {
        var excluded = codes.ToHashSet(CodeComparer);
        var removedLines = document.Signals
            .Where(signal => excluded.Contains(signal.Code))
            .Select(signal => signal.LineNumber - 1)
            .ToHashSet();
        return string.Join(Environment.NewLine,
            document.Lines.Where((_, index) => !removedLines.Contains(index)));
    }

    public static string Merge(KksDocument target, KksDocument source)
    {
        var signals = target.Signals.Concat(source.Signals)
            .GroupBy(signal => signal.Code, CodeComparer)
            .Select(group => group.First());
        return ExportSignals(signals);
    }

    public static string ExportIntersection(KksDocument first, KksDocument second)
    {
        var secondCodes = second.Signals.Select(x => x.Code).ToHashSet(CodeComparer);
        return ExportSignals(first.Signals.Where(x => secondCodes.Contains(x.Code))
            .GroupBy(x => x.Code, CodeComparer).Select(x => x.First()));
    }

    public static string ExportSymmetricDifference(KksDocument first, KksDocument second)
    {
        var firstCodes = first.Signals.Select(x => x.Code).ToHashSet(CodeComparer);
        var secondCodes = second.Signals.Select(x => x.Code).ToHashSet(CodeComparer);
        var signals = first.Signals.Where(x => !secondCodes.Contains(x.Code))
            .Concat(second.Signals.Where(x => !firstCodes.Contains(x.Code)))
            .GroupBy(x => x.Code, CodeComparer).Select(x => x.First());
        return ExportSignals(signals);
    }

    public static string ExportUnion(KksDocument first, KksDocument second) => Merge(first, second);

    private static string ExportSignals(IEnumerable<KksSignal> source)
    {
        var signals = source.GroupBy(x => x.Code, CodeComparer).Select(x => x.First()).ToList();
        if (signals.Count == 0) return string.Empty;

        if (signals.Any(x => x.Section == KksSection.Unknown))
            return string.Join(Environment.NewLine, signals.OrderBy(x => x.Code, CodeComparer).Select(x => x.Code));

        var lines = new List<string>();
        AppendSection(lines, "#IA1000", signals.Where(x => x.Section == KksSection.Analog));
        AppendSection(lines, "#ID1000", signals.Where(x => x.Section == KksSection.Discrete));
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendSection(List<string> lines, string header, IEnumerable<KksSignal> source)
    {
        var codes = source.Select(x => x.Code).OrderBy(x => x, CodeComparer).ToList();
        if (codes.Count == 0) return;
        if (lines.Count > 0) lines.Add(string.Empty);
        lines.Add(header);
        lines.AddRange(codes);
        lines.Add("#");
    }

    private static IReadOnlyList<SuffixGroup> BuildSuffixGroups(IEnumerable<KksSignal> signals, KksSection section) =>
        signals.Where(x => x.Section == section && x.Suffix is not null)
            .GroupBy(x => x.Suffix!, CodeComparer)
            .Select(x => new SuffixGroup(
                x.Key,
                section,
                x.Count(),
                string.Join(", ", x.Take(3).Select(s => s.Code))))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Suffix, CodeComparer)
            .ToList();

    public static string SectionName(KksSection section) => section switch
    {
        KksSection.Analog => "Аналоговый (#IA1000)",
        KksSection.Discrete => "Дискретный (#ID1000)",
        _ => "Неизвестно"
    };
}
