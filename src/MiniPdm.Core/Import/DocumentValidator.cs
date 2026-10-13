using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Import;

public sealed record ValidationResult(string? Error, IReadOnlyList<string> Warnings);

/// <summary>Проверки, которым достаточно одного документа (без контекста остального набора).</summary>
public static class DocumentValidator
{
    public static ValidationResult Validate(CadDocument d)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(d.Name))
            return Fail("Не указано наименование");

        if (d.Type is ObjectType.Assembly or ObjectType.Part)
        {
            if (string.IsNullOrWhiteSpace(d.Designation))
                return Fail("Не указано обозначение");
            if (!Designation.IsValid(d.Designation))
                return Fail($"Обозначение «{d.Designation}» не соответствует формату АБВГ.123456.789");
        }
        else if (!string.IsNullOrWhiteSpace(d.Designation))
        {
            warnings.Add("У стандартного изделия обозначение не используется и игнорируется");
        }

        if (d.Type == ObjectType.Assembly)
        {
            if (d.Mass is not null) warnings.Add("Масса сборки вычисляется по составу, указанное значение игнорируется");
            if (d.Components.Count == 0) warnings.Add("Сборка не содержит компонентов");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in d.Components)
            {
                if (string.IsNullOrWhiteSpace(c.File)) return Fail("У компонента не указан файл");
                if (c.Count <= 0) return Fail($"Компонент «{c.File}»: количество {c.Count} должно быть больше нуля");
                if (!seen.Add(c.File)) return Fail($"Компонент «{c.File}» указан в составе повторно");
            }
        }
        else
        {
            if (d.Components.Count > 0) return Fail("Деталь и стандартное изделие не могут иметь состав");
            if (d.Mass is null) warnings.Add("Не указана масса");
            else if (d.Mass <= 0) return Fail($"Масса {d.Mass} должна быть больше нуля");
        }

        return new ValidationResult(null, warnings);

        static ValidationResult Fail(string error) => new(error, Array.Empty<string>());
    }
}
