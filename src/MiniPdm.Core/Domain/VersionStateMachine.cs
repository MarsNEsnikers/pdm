namespace MiniPdm.Core.Domain;

public sealed class InvalidStateTransitionException(VersionState from, VersionState to)
    : InvalidOperationException($"Переход «{DisplayNames.Of(from)}» → «{DisplayNames.Of(to)}» недопустим");

public static class VersionStateMachine
{
    public static bool CanTransition(VersionState from, VersionState to) =>
        (from, to) is (VersionState.InWork, VersionState.Approved)
                   or (VersionState.InWork, VersionState.Annulled)
                   or (VersionState.Approved, VersionState.Annulled);

    public static void EnsureTransition(VersionState from, VersionState to)
    {
        if (!CanTransition(from, to)) throw new InvalidStateTransitionException(from, to);
    }

    /// <summary>Изменять данные можно только у версии «В работе».</summary>
    public static bool CanModify(VersionState state) => state == VersionState.InWork;
}
