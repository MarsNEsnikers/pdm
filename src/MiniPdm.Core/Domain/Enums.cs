namespace MiniPdm.Core.Domain;

public enum ObjectType { Assembly, Part, StandardPart }

public enum VersionState { InWork, Approved, Annulled }

public static class DisplayNames
{
    public static string Of(ObjectType t) => t switch
    {
        ObjectType.Assembly => "Сборка",
        ObjectType.Part => "Деталь",
        ObjectType.StandardPart => "Стандартное изделие",
        _ => t.ToString()
    };

    public static string Of(VersionState s) => s switch
    {
        VersionState.InWork => "В работе",
        VersionState.Approved => "Утверждено",
        VersionState.Annulled => "Аннулировано",
        _ => s.ToString()
    };
}
