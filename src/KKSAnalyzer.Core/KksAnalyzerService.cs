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

        var analogSuffixes = BuildSuffixGroups(document.Signals, KksSection.Analog);
        var discreteSuffixes = BuildSuffixGroups(document.Signals, KksSection.Discrete);

        return new FileAnalysis
        {
            Document = document,
            Duplicates = duplicates,
            WithoutSuffix = document.Signals.Where(x => x.Suffix is null).ToList(),
            AnalogSuffixes = analogSuffixes,
            DiscreteSuffixes = discreteSuffixes,
            CommonSuffixes = BuildSuffixIntersection(analogSuffixes, discreteSuffixes)
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

    public static SectionConsistencyResult CompareSections(KksDocument reference, KksDocument checkedDocument)
    {
        var referenceByCode = reference.Signals
            .Where(x => x.Section is KksSection.Analog or KksSection.Discrete)
            .GroupBy(x => x.Code, CodeComparer)
            .ToDictionary(x => x.Key, x => x.First(), CodeComparer);
        var checkedByCode = checkedDocument.Signals
            .Where(x => x.Section is KksSection.Analog or KksSection.Discrete)
            .GroupBy(x => x.Code, CodeComparer)
            .ToDictionary(x => x.Key, x => x.First(), CodeComparer);

        var commonCodes = referenceByCode.Keys.Intersect(checkedByCode.Keys, CodeComparer).ToList();
        var mismatches = commonCodes
            .Where(code => referenceByCode[code].Section != checkedByCode[code].Section)
            .Select(code => new SectionMismatch(
                referenceByCode[code].Code,
                referenceByCode[code].Section,
                checkedByCode[code].Section,
                referenceByCode[code].LineNumber,
                checkedByCode[code].LineNumber))
            .OrderBy(x => x.Code, CodeComparer)
            .ToList();

        return new SectionConsistencyResult
        {
            CommonSignalCount = commonCodes.Count,
            Mismatches = mismatches
        };
    }

    public static string MoveSignalsToSections(
        KksDocument document,
        IEnumerable<(string Code, KksSection TargetSection)> requestedMoves)
    {
        var targets = requestedMoves
            .Where(x => x.TargetSection is KksSection.Analog or KksSection.Discrete)
            .GroupBy(x => x.Code, CodeComparer)
            .ToDictionary(x => x.Key, x => x.Last().TargetSection, CodeComparer);
        if (targets.Count == 0)
            return string.Join(Environment.NewLine, document.Lines);

        var linesToRemove = document.Signals
            .Where(x => targets.TryGetValue(x.Code, out var target) && x.Section != target)
            .Select(x => x.LineNumber - 1)
            .ToHashSet();
        var existingInTarget = document.Signals
            .Where(x => targets.TryGetValue(x.Code, out var target) && x.Section == target)
            .Select(x => x.Code)
            .ToHashSet(CodeComparer);
        var additions = targets
            .Where(x => !existingInTarget.Contains(x.Key) && document.Signals.Any(signal =>
                CodeComparer.Equals(signal.Code, x.Key) && signal.Section != x.Value))
            .GroupBy(x => x.Value)
            .ToDictionary(
                x => x.Key,
                x => x.Select(item => item.Key).OrderBy(code => code, CodeComparer).ToList());

        var output = new List<string>();
        var currentSection = KksSection.Unknown;
        var insertedSections = new HashSet<KksSection>();

        void AppendPending(KksSection section)
        {
            if (insertedSections.Contains(section) || !additions.TryGetValue(section, out var codes)) return;
            output.AddRange(codes);
            insertedSections.Add(section);
        }

        for (var index = 0; index < document.Lines.Length; index++)
        {
            var trimmed = document.Lines[index].Trim();
            if (trimmed.StartsWith('#'))
            {
                AppendPending(currentSection);
                currentSection = KksParser.TryParseSectionHeader(trimmed, out var parsedSection)
                    ? parsedSection
                    : KksSection.Unknown;
            }

            if (!linesToRemove.Contains(index))
                output.Add(document.Lines[index]);
        }

        AppendPending(currentSection);
        foreach (var section in new[] { KksSection.Analog, KksSection.Discrete })
        {
            if (insertedSections.Contains(section) || !additions.TryGetValue(section, out var codes)) continue;
            if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1])) output.Add(string.Empty);
            output.Add(section == KksSection.Analog ? "#IA1000" : "#ID1000");
            output.AddRange(codes);
            output.Add("#");
        }

        return string.Join(Environment.NewLine, output);
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

    public static string ExportCommonByReferenceSections(KksDocument reference, KksDocument second)
    {
        var secondCodes = second.Signals.Select(x => x.Code).ToHashSet(CodeComparer);
        var commonSignals = reference.Signals
            .Where(x => secondCodes.Contains(x.Code) && x.Section is KksSection.Analog or KksSection.Discrete)
            .GroupBy(x => x.Code, CodeComparer)
            .Select(x => x.First());
        return ExportSignals(commonSignals, reference);
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

    private static string ExportSignals(IEnumerable<KksSignal> source, KksDocument? headerSource = null)
    {
        var signals = source.GroupBy(x => x.Code, CodeComparer).Select(x => x.First()).ToList();
        if (signals.Count == 0) return string.Empty;

        if (signals.Any(x => x.Section == KksSection.Unknown))
            return string.Join(Environment.NewLine, signals.OrderBy(x => x.Code, CodeComparer).Select(x => x.Code));

        var lines = new List<string>();
        AppendSection(lines, FindSectionHeader(headerSource, KksSection.Analog) ?? "#IA1000",
            signals.Where(x => x.Section == KksSection.Analog));
        AppendSection(lines, FindSectionHeader(headerSource, KksSection.Discrete) ?? "#ID1000",
            signals.Where(x => x.Section == KksSection.Discrete));
        return string.Join(Environment.NewLine, lines);
    }

    private static string? FindSectionHeader(KksDocument? document, KksSection section) => document?.Lines
        .Select(x => x.Trim())
        .FirstOrDefault(x => KksParser.TryParseSectionHeader(x, out var parsedSection) && parsedSection == section);

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
            .Select(x =>
            {
                var codes = x.Select(signal => signal.Code)
                    .OrderBy(code => code, CodeComparer)
                    .ToList();
                return new SuffixGroup(
                    x.Key,
                    section,
                    codes.Count,
                    string.Join(", ", codes.Take(3)),
                    codes);
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Suffix, CodeComparer)
            .ToList();

    private static IReadOnlyList<SuffixIntersection> BuildSuffixIntersection(
        IEnumerable<SuffixGroup> analogSuffixes,
        IEnumerable<SuffixGroup> discreteSuffixes)
    {
        var discreteBySuffix = discreteSuffixes.ToDictionary(x => x.Suffix, CodeComparer);

        return analogSuffixes
            .Where(analog => discreteBySuffix.ContainsKey(analog.Suffix))
            .Select(analog =>
            {
                var discrete = discreteBySuffix[analog.Suffix];
                return new SuffixIntersection(
                    analog.Suffix,
                    analog.Count,
                    discrete.Count,
                    analog.Examples,
                    discrete.Examples,
                    analog.Codes,
                    discrete.Codes);
            })
            .OrderBy(x => x.Suffix, CodeComparer)
            .ToList();
    }

    public static string SectionName(KksSection section) => section switch
    {
        KksSection.Analog => "Аналоговый (#IA…)",
        KksSection.Discrete => "Дискретный (#ID…)",
        _ => "Неизвестно"
    };
}
