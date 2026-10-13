using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Cad;

/// <summary>
/// Единственная точка доступа к CAD-системе. Реализация (JSON-файлы, API КОМПАС-3D) скрыта за интерфейсом;
/// «location» непрозрачный адрес набора документов (для эмуляции папка), «documentId»  идентификатор
/// документа внутри набора (для эмуляции имя файла).
/// </summary>
public interface ICadDocumentReader
{
    Task<IReadOnlyList<string>> ListDocumentsAsync(string location, CancellationToken ct);
    Task<CadDocument> ReadAsync(string location, string documentId, CancellationToken ct);
}

public sealed record CadComponent(string File, int Count);

public sealed record CadDocument(
    string FileName,
    ObjectType Type,
    string? Designation,
    string Name,
    string? Material,
    decimal? Mass,
    IReadOnlyList<CadComponent> Components);

/// <summary>Документ нельзя прочитать (повреждён, неизвестный формат и т. п.).</summary>
public sealed class CadDocumentException : Exception
{
    public CadDocumentException(string message) : base(message) { }
    public CadDocumentException(string message, Exception inner) : base(message, inner) { }
}
