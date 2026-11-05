using System.Reflection;
using Xunit;

namespace CarKit.Maui.Core.Tests.Architecture;

public class AssemblyRulesTests
{
    public static TheoryData<string> MauiFreeAssemblies => ["CarKit.Maui.Core", "CarKit.Maui.Testing"];

    [Theory, MemberData(nameof(MauiFreeAssemblies))]
    public void Assembly_does_not_reference_Maui(string name) =>
        Assert.DoesNotContain(Assembly.Load(name).GetReferencedAssemblies(),
            r => r.Name!.StartsWith("Microsoft.Maui", StringComparison.Ordinal));

    [Theory, MemberData(nameof(MauiFreeAssemblies))]
    public void Assembly_is_marked_trimmable(string name) =>
        Assert.Contains(Assembly.Load(name).GetCustomAttributes<AssemblyMetadataAttribute>(),
            a => a.Key == "IsTrimmable" && a.Value == "True");
}
