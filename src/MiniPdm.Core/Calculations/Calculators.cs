using MiniPdm.Core.Data;
using MiniPdm.Core.Domain;

namespace MiniPdm.Core.Calculations;

public sealed class CyclicReferenceException(string message) : InvalidOperationException(message);

public sealed record MassResult(
    decimal? TotalKg, IReadOnlyList<string> MissingMass, IReadOnlySet<long> MissingObjectIds)
{
    public bool IsComplete => TotalKg is not null;
}

public static class MassCalculator
{
    /// <summary>
    /// Масса одной штуки сборки с учётом вложенности и количеств. Если у каких-то компонентов
    /// массы нет итог null, а список MissingMass называет эти компоненты.
    /// </summary>
    public static MassResult Calculate(TreeNode root)
    {
        var missing = new List<string>();
        var missingIds = new HashSet<long>();
        var total = Calc(root, missing, missingIds, new HashSet<long>());
        return new MassResult(missing.Count == 0 ? total : null, missing.Distinct().ToList(), missingIds);
    }

    private static decimal Calc(TreeNode node, List<string> missing, HashSet<long> missingIds, HashSet<long> stack)
    {
        if (node.IsCycle || !stack.Add(node.ObjectId))
            throw new CyclicReferenceException($"Циклическая ссылка на «{node.DisplayName}»");
        try
        {
            if (!node.HasCurrentVersion)
            {
                missing.Add($"{node.DisplayName} (нет действующей версии)");
                missingIds.Add(node.ObjectId);
                return 0;
            }
            if (node.Type != ObjectType.Assembly)
            {
                if (node.MassKg is null) { missing.Add(node.DisplayName); missingIds.Add(node.ObjectId); return 0; }
                return node.MassKg.Value;
            }
            decimal sum = 0;
            foreach (var child in node.Children)
                sum += child.Quantity * Calc(child, missing, missingIds, stack);
            return sum;
        }
        finally { stack.Remove(node.ObjectId); }
    }
}

public sealed record BomLine(
    long ObjectId, ObjectType Type, string? Designation, string Name,
    long Quantity, decimal? UnitMassKg, decimal? TotalMassKg);

public static class BomBuilder
{
    /// <summary>
    /// Плоская спецификация: все детали и стандартные изделия, количества перемножаются по пути
    /// и суммируются для одинаковых объектов на разных уровнях.
    /// </summary>
    public static IReadOnlyList<BomLine> Build(TreeNode root)
    {
        var acc = new Dictionary<long, (TreeNode Node, long Qty)>();
        Walk(root, 1, acc, new HashSet<long> { root.ObjectId });

        return acc.Values
            .Select(x => new BomLine(x.Node.ObjectId, x.Node.Type, x.Node.Designation, x.Node.Name, x.Qty,
                x.Node.MassKg, x.Node.MassKg is null ? null : x.Node.MassKg * x.Qty))
            .OrderBy(l => l.Type)
            .ThenBy(l => l.Designation ?? l.Name, StringComparer.Ordinal)
            .ThenBy(l => l.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static void Walk(TreeNode node, long multiplier,
        Dictionary<long, (TreeNode Node, long Qty)> acc, HashSet<long> stack)
    {
        foreach (var child in node.Children)
        {
            if (child.IsCycle || stack.Contains(child.ObjectId))
                throw new CyclicReferenceException($"Циклическая ссылка на «{child.DisplayName}»");

            var qty = multiplier * child.Quantity;
            if (child.Type == ObjectType.Assembly && child.HasCurrentVersion)
            {
                stack.Add(child.ObjectId);
                Walk(child, qty, acc, stack);
                stack.Remove(child.ObjectId);
            }
            else
            {
                acc[child.ObjectId] = acc.TryGetValue(child.ObjectId, out var e)
                    ? (e.Node, e.Qty + qty)
                    : (child, qty);
            }
        }
    }
}

public static class CycleDetector
{
    /// <summary>Возвращает все узлы, лежащие на циклах (компоненты сильной связности Тарьяна).</summary>
    public static IReadOnlySet<string> FindNodesInCycles(IReadOnlyDictionary<string, IReadOnlyCollection<string>> graph)
    {
        var index = 0;
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new HashSet<string>(StringComparer.Ordinal);

        void Strong(string v)
        {
            idx[v] = low[v] = index++;
            stack.Push(v); onStack.Add(v);
            foreach (var w in graph[v])
            {
                if (!graph.ContainsKey(w)) continue;
                if (!idx.ContainsKey(w)) { Strong(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack.Contains(w)) low[v] = Math.Min(low[v], idx[w]);
            }
            if (low[v] != idx[v]) return;
            var comp = new List<string>();
            string x;
            do { x = stack.Pop(); onStack.Remove(x); comp.Add(x); } while (x != v);
            if (comp.Count > 1 || graph[v].Contains(v))
                foreach (var c in comp) result.Add(c);
        }

        foreach (var v in graph.Keys)
            if (!idx.ContainsKey(v)) Strong(v);
        return result;
    }
}
