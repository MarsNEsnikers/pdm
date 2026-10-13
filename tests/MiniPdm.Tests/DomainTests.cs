using MiniPdm.Core.Domain;
using MiniPdm.Core.Import;

namespace MiniPdm.Tests;

public class DesignationTests
{
    [Theory]
    [InlineData("АБВГ.301245.001")]
    [InlineData("РДЦЛ.304112.300")]
    [InlineData("ЁЖЗИ.000000.999")]
    public void Valid(string value) => Assert.True(Designation.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD.301245.001")]      // латиница
    [InlineData("абвг.301245.001")]      // строчные
    [InlineData("АБВ.301245.001")]       // 3 буквы
    [InlineData("АБВГД.301245.001")]     // 5 букв
    [InlineData("АБВГ.30124.001")]       // 5 цифр
    [InlineData("АБВГ.301245.01")]       // 2 цифры в конце
    [InlineData("АБВГ.301245.001 ")]     // пробел
    [InlineData("АБВГ.301245.001\n")]    // перевод строки
    [InlineData("АБВГ-301245-001")]
    public void Invalid(string? value) => Assert.False(Designation.IsValid(value));
}

public class VersionStateMachineTests
{
    [Theory]
    [InlineData(VersionState.InWork, VersionState.Approved)]
    [InlineData(VersionState.InWork, VersionState.Annulled)]
    [InlineData(VersionState.Approved, VersionState.Annulled)]
    public void AllowedTransitions(VersionState from, VersionState to) =>
        Assert.True(VersionStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(VersionState.Approved, VersionState.InWork)]
    [InlineData(VersionState.Annulled, VersionState.InWork)]
    [InlineData(VersionState.Annulled, VersionState.Approved)]
    [InlineData(VersionState.InWork, VersionState.InWork)]
    [InlineData(VersionState.Approved, VersionState.Approved)]
    [InlineData(VersionState.Annulled, VersionState.Annulled)]
    public void ForbiddenTransitions(VersionState from, VersionState to)
    {
        Assert.False(VersionStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidStateTransitionException>(() => VersionStateMachine.EnsureTransition(from, to));
    }

    [Fact]
    public void OnlyInWorkIsModifiable()
    {
        Assert.True(VersionStateMachine.CanModify(VersionState.InWork));
        Assert.False(VersionStateMachine.CanModify(VersionState.Approved));
        Assert.False(VersionStateMachine.CanModify(VersionState.Annulled));
    }
}

public class DocumentValidatorTests
{
    [Fact]
    public void PartWithoutMass_IsWarning()
    {
        var r = DocumentValidator.Validate(Docs.Part("a.m3d", "АБВГ.000001.001", mass: null));
        Assert.Null(r.Error);
        Assert.Contains(r.Warnings, w => w.Contains("масса"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveMass_IsError(int mass) =>
        Assert.NotNull(DocumentValidator.Validate(Docs.Part("a.m3d", "АБВГ.000001.001", mass)).Error);

    [Fact]
    public void BadDesignation_IsError() =>
        Assert.NotNull(DocumentValidator.Validate(Docs.Part("a.m3d", "bad")).Error);

    [Fact]
    public void AssemblyWithoutDesignation_IsError() =>
        Assert.NotNull(DocumentValidator.Validate(Docs.Asm("a.a3d", " ")).Error);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveQuantity_IsError(int count) =>
        Assert.NotNull(DocumentValidator.Validate(Docs.Asm("a.a3d", "АБВГ.000001.001", ("x.m3d", count))).Error);

    [Fact]
    public void DuplicateComponent_IsError() =>
        Assert.NotNull(DocumentValidator.Validate(
            Docs.Asm("a.a3d", "АБВГ.000001.001", ("x.m3d", 1), ("x.m3d", 2))).Error);

    [Fact]
    public void EmptyAssembly_IsWarning()
    {
        var r = DocumentValidator.Validate(Docs.Asm("a.a3d", "АБВГ.000001.001"));
        Assert.Null(r.Error);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void StandardPartWithoutDesignation_IsValid() =>
        Assert.Null(DocumentValidator.Validate(Docs.Std("Болт.m3d")).Error);

    [Fact]
    public void BlankName_IsError() =>
        Assert.NotNull(DocumentValidator.Validate(Docs.Part("a.m3d", "АБВГ.000001.001", 1, " ") with { Name = " " }).Error);
}
