using AnoCore.Abstractions.Modules;

namespace AnoCore.Tests.Runtime;

[TestClass]
public sealed class AbstractionsDependencyTests
{
    [TestMethod]
    public void ModuleSdk_DoesNotReferenceRuntimeEngineOrPersistenceAssemblies()
    {
        var references = typeof(IAnoModule).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        CollectionAssert.DoesNotContain(references, "AnoCore.Runtime");
        CollectionAssert.DoesNotContain(references, "AnoCore.Plugin");
        CollectionAssert.DoesNotContain(references, "CounterStrikeSharp.API");
        CollectionAssert.DoesNotContain(references, "MySqlConnector");
    }
}
