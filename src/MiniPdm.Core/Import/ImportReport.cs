using System.Text;

namespace MiniPdm.Core.Import;

public enum ReportSeverity { Info, Warning, Error }

public sealed record ImportReportEntry(string FileName, ReportSeverity Severity, string Reason, string? Action);

public sealed record ImportProgress(string Stage, int Done, int Total);

public sealed class ImportReport
{
    public ImportReport(IReadOnlyList<ImportReportEntry> entries, string location = "")
    {
        Entries = entries;
        Location = location;
    }

    public string Location { get; }
    public IReadOnlyList<ImportReportEntry> Entries { get; }
    /// <summary>Принято: без ошибок (включая принятые с предупреждением и без изменений).</summary>
    public int Accepted => Entries.Count(e => e.Severity != ReportSeverity.Error);
    public int Rejected => Entries.Count(e => e.Severity == ReportSeverity.Error);
    public int WithWarnings => Entries.Count(e => e.Severity == ReportSeverity.Warning);
}

public static class ImportReportFormatter
{
    public static string ToDisplay(this ReportSeverity s) => s switch
    {
        ReportSeverity.Error => "Ошибка",
        ReportSeverity.Warning => "Предупреждение",
        _ => "Принят"
    };

    /// <summary>CSV с разделителем «;» открывается в Excel с русской локалью.</summary>
    public static string ToCsv(ImportReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Файл;Результат;Причина;Действие");
        foreach (var e in report.Entries)
            sb.AppendLine(string.Join(';', Escape(e.FileName), Escape(e.Severity.ToDisplay()), Escape(e.Reason), Escape(e.Action ?? "")));
        sb.AppendLine();
        sb.AppendLine($"Принято;{report.Accepted}");
        sb.AppendLine($"Отклонено;{report.Rejected}");
        sb.AppendLine($"С предупреждением;{report.WithWarnings}");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
