using MiniPdm.Core.Calculations;
using MiniPdm.Core.Domain;
using static MiniPdm.Tests.TreeBuilder;

namespace MiniPdm.Tests;

public class MassCalculatorTests
{
    [Fact]
    public void NestedAssemblies_MultiplyQuantities()
    {
        // Корень: 2 × подсборка(3 × деталь 1.5 + 1 × стандартное 0.25) + 4 × деталь 2
        var sub = Sub("sub", 2, 2,
            Node("p", mass: 1.5m, qty: 3, id: 3), Node("s", ObjectType.StandardPart, 0.25m, id: 4));
        var root = Asm("root", 1, sub, Node("q", mass: 2m, qty: 4, id: 5));

        var result = MassCalculator.Calculate(root);

        Assert.Equal(2 * (3 * 1.5m + 0.25m) + 4 * 2m, result.TotalKg);
        Assert.Empty(result.MissingMass);
    }

    [Fact]
    public void MissingMass_IsReported_NotZero()
    {
        var root = Asm("root", 1,
            Node("ok", mass: 1m, id: 2),
            Node("no-mass", mass: null, id: 3));

        var result = MassCalculator.Calculate(root);

        Assert.Null(result.TotalKg);
        Assert.False(result.IsComplete);
        Assert.Equal(new[] { "no-mass" }, result.MissingMass);
        Assert.Equal(new long[] { 3 }, result.MissingObjectIds.ToArray());
    }

    [Fact]
    public void ComponentWithoutCurrentVersion_IsReportedAsMissing()
    {
        var root = Asm("root", 1, new TreeNodeFactory().Annulled());
        var result = MassCalculator.Calculate(root);
        Assert.Null(result.TotalKg);
        Assert.Single(result.MissingMass);
    }

    [Fact]
    public void EmptyAssembly_HasZeroMass() =>
        Assert.Equal(0m, MassCalculator.Calculate(Asm("root", 1)).TotalKg);

    [Fact]
    public void CycleFlaggedNode_Throws()
    {
        var root = Asm("root", 1);
        root.Children.Add(new() { ObjectId = 1, Type = ObjectType.Assembly, Name = "root", VersionNo = 1, IsCycle = true });
        Assert.Throws<CyclicReferenceException>(() => MassCalculator.Calculate(root));
    }

    [Fact]
    public void ReferenceCycleInObjectGraph_Throws()
    {
        var a = Asm("a", 1);
        var b = Asm("b", 2);
        a.Children.Add(b);
        b.Children.Add(a);
        Assert.Throws<CyclicReferenceException>(() => MassCalculator.Calculate(a));
    }

    private sealed class TreeNodeFactory
    {
        public MiniPdm.Core.Data.TreeNode Annulled() => Node("gone", hasVersion: false, id: 9);
    }
}

public class BomBuilderTests
{
    [Fact]
    public void SamePartOnDifferentLevels_QuantitiesMultiplyAlongPathAndSum()
    {
        // болт: 2 прямо в корне + (3 подсборок × 4 болта) = 2 + 12 = 14
        var sub = Sub("sub", 2, 3, Node("bolt", ObjectType.StandardPart, 0.01m, qty: 4, id: 10));
        var root = Asm("root", 1, Node("bolt", ObjectType.StandardPart, 0.01m, qty: 2, id: 10), sub);

        var bom = BomBuilder.Build(root);

        var line = Assert.Single(bom);
        Assert.Equal(14, line.Quantity);
        Assert.Equal(0.14m, line.TotalMassKg);
    }

    [Fact]
    public void LineWithoutMass_HasNullMass_AndAssembliesAreNotListed()
    {
        var root = Asm("root", 1,
            Asm("sub", 2, Node("p", mass: null, qty: 2, id: 3)));

        var bom = BomBuilder.Build(root);

        var line = Assert.Single(bom);
        Assert.Null(line.UnitMassKg);
        Assert.Null(line.TotalMassKg);
        Assert.Equal(2, line.Quantity);
    }

    [Fact]
    public void Cycle_Throws()
    {
        var a = Asm("a", 1);
        var b = Asm("b", 2);
        a.Children.Add(b);
        b.Children.Add(a);
        Assert.Throws<CyclicReferenceException>(() => BomBuilder.Build(a));
    }
}

public class CycleDetectorTests
{
    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> G(params (string, string[])[] edges) =>
        edges.ToDictionary(e => e.Item1, e => (IReadOnlyCollection<string>)e.Item2);

    [Fact]
    public void Acyclic_ReturnsEmpty() =>
        Assert.Empty(CycleDetector.FindNodesInCycles(G(("a", ["b", "c"]), ("b", ["c"]), ("c", []))));

    [Fact]
    public void Diamond_IsNotACycle() =>
        Assert.Empty(CycleDetector.FindNodesInCycles(G(("a", ["b", "c"]), ("b", ["d"]), ("c", ["d"]), ("d", []))));

    [Fact]
    public void SelfLoop_IsCycle() =>
        Assert.Equal(new[] { "a" }, CycleDetector.FindNodesInCycles(G(("a", ["a"]))));

    [Fact]
    public void OnlyNodesOnCycleAreReturned()
    {
        // a → b → c → b ; d → a (d и a ведут в цикл, но сами не лежат на нём)
        var nodes = CycleDetector.FindNodesInCycles(G(("d", ["a"]), ("a", ["b"]), ("b", ["c"]), ("c", ["b"])));
        Assert.Equal(new[] { "b", "c" }, nodes.OrderBy(x => x));
    }

    [Fact]
    public void ReferencesToUnknownNodes_AreIgnored() =>
        Assert.Empty(CycleDetector.FindNodesInCycles(G(("a", ["missing"]))));
}
