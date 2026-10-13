using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPdm.Core.Cad;
using MiniPdm.Core.Domain;

namespace MiniPdm.Infrastructure.Cad;

/// <summary>Эмуляция CAD-системы: документы JSON-файлы (*.a3d, *.m3d) в папке.</summary>
public sealed class JsonCadDocumentReader : ICadDocumentReader
{
    private static readonly string[] Extensions = [".a3d", ".m3d"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public Task<IReadOnlyList<string>> ListDocumentsAsync(string location, CancellationToken ct)
    {
        if (!Directory.Exists(location))
            throw new DirectoryNotFoundException($"Папка не найдена: {location}");

        IReadOnlyList<string> files = Directory.EnumerateFiles(location)
            .Select(Path.GetFileName)
            .Where(n => n is not null && Extensions.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(files);
    }

    public async Task<CadDocument> ReadAsync(string location, string documentId, CancellationToken ct)
    {
        var path = Path.Combine(location, documentId);
        Dto? dto;
        try
        {
            await using var stream = File.OpenRead(path);
            dto = await JsonSerializer.DeserializeAsync<Dto>(stream, Options, ct);
        }
        catch (JsonException ex) { throw new CadDocumentException($"Некорректный JSON: {ex.Message}", ex); }
        catch (IOException ex) { throw new CadDocumentException($"Не удалось прочитать файл: {ex.Message}", ex); }

        if (dto is null) throw new CadDocumentException("Пустой документ");
        if (dto.FormatVersion != 1) throw new CadDocumentException($"Неподдерживаемая версия формата: {dto.FormatVersion}");
        if (!Enum.TryParse<ObjectType>(dto.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            throw new CadDocumentException($"Неизвестный тип документа «{dto.Type}»");

        return new CadDocument(
            documentId, type, dto.Designation, dto.Name ?? "",
            dto.Properties?.Material, dto.Properties?.Mass,
            (dto.Components ?? []).Select(c => new CadComponent(c.File ?? "", c.Count ?? 0)).ToList());
    }

    private sealed class Dto
    {
        public int FormatVersion { get; set; }
        public string? Type { get; set; }
        public string? Designation { get; set; }
        public string? Name { get; set; }
        public PropertiesDto? Properties { get; set; }
        public List<ComponentDto>? Components { get; set; }
    }

    private sealed class PropertiesDto
    {
        public string? Material { get; set; }
        public decimal? Mass { get; set; }
    }

    private sealed class ComponentDto
    {
        public string? File { get; set; }
        public int? Count { get; set; }
    }
}
