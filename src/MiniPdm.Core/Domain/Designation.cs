using System.Text.RegularExpressions;

namespace MiniPdm.Core.Domain;

/// <summary>Упрощённое обозначение ЕСКД: АБВГ.123456.789.</summary>
public static partial class Designation
{
    [GeneratedRegex(@"^[А-ЯЁ]{4}\.[0-9]{6}\.[0-9]{3}\z")]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);
}
