using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using TapeCraft.Domain;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;

namespace TapeCraft.UnitTests.Architecture;

/// <summary>The rules of ADR 0001 (docs/adr/0001-stack-and-architecture.md).</summary>
public sealed class ArchitectureTests
{
    private const string Features = "TapeCraft.Api.Features";

    private static readonly ArchitectureModel App =
        new ArchLoader().LoadAssemblies(typeof(Program).Assembly, typeof(User).Assembly).Build();

    public static TheoryData<string> FeatureNames()
    {
        var names = typeof(Program).Assembly.GetTypes()
            .Select(type => type.Namespace ?? "")
            .Where(ns => ns.StartsWith(Features + ".", StringComparison.Ordinal))
            .Select(ns => ns[(Features.Length + 1)..].Split('.')[0])
            .Distinct()
            .Order();
        return [.. names];
    }

    [Fact]
    public void The_domain_uses_only_the_base_library()
    {
        var references = typeof(User).Assembly.GetReferencedAssemblies().Select(name => name.Name ?? "");
        Assert.All(references, name => Assert.True(name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal), name));
    }

    [Fact]
    public void There_is_at_least_one_feature() => Assert.NotEmpty(FeatureNames());

    [Theory]
    [MemberData(nameof(FeatureNames))]
    public void A_feature_does_not_use_another_feature(string feature)
    {
        var own = $@"^{Features.Replace(".", @"\.")}\.{feature}(\..+)?$";
        var others = $@"^{Features.Replace(".", @"\.")}\.(?!{feature}(\.|$)).+$";
        Types().That().ResideInNamespaceMatching(own)
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(others))
            .WithoutRequiringPositiveResults()
            .Check(App);
    }

    [Fact]
    public void Features_use_the_outside_world_only_through_interfaces() =>
        Types().That().ResideInNamespaceMatching($@"^{Features.Replace(".", @"\.")}(\..+)?$")
            .Should().NotDependOnAny(Classes().That().ResideInNamespaceMatching(@"^TapeCraft\.Api\.Infrastructure(\..+)?$"))
            .Check(App);

    [Fact]
    public void Infrastructure_and_web_plumbing_do_not_use_features() =>
        Types().That().ResideInNamespaceMatching(@"^TapeCraft\.Api\.(Infrastructure|Web)(\..+)?$")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching($@"^{Features.Replace(".", @"\.")}(\..+)?$"))
            .Check(App);
}
