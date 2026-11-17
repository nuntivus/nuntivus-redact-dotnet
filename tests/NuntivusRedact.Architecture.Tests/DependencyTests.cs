using System.Reflection;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Detectors.Regex;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Architecture.Tests;

public sealed class DependencyTests
{
    [Fact]
    public void ProductAssembliesDependInwardWithoutCyclesOrDiInCore()
    {
        Assembly[] assemblies = [typeof(Detection).Assembly, typeof(RedactionEngine).Assembly,
            typeof(PersonalDataDetectors).Assembly, typeof(RegexDetector).Assembly,
            typeof(FixedReplacementStrategy).Assembly, typeof(JsonDocumentProcessor).Assembly,
            typeof(TextDocumentProcessor).Assembly];
        var allowed = new Dictionary<string, string[]>
        {
            ["NuntivusRedact.Abstractions"] = [],
            ["NuntivusRedact.Core"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Detectors.Regex"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Detectors.PersonalData"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Detectors.Regex"],
            ["NuntivusRedact.Strategies"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Formats.Text"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Formats.Json"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Formats.Text"]
        };
        foreach (var assembly in assemblies)
            foreach (var reference in assembly.GetReferencedAssemblies().Where(r => r.Name!.StartsWith("NuntivusRedact.", StringComparison.Ordinal)))
                Assert.Contains(reference.Name!, allowed[assembly.GetName().Name!]);
        Assert.DoesNotContain(typeof(RedactionEngine).Assembly.GetReferencedAssemblies(), r => r.Name!.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        void Visit(string name)
        {
            Assert.True(visiting.Add(name), "Circular product dependency.");
            foreach (var dependency in allowed[name]) if (!visited.Contains(dependency)) Visit(dependency);
            visiting.Remove(name); visited.Add(name);
        }
        foreach (var name in allowed.Keys) if (!visited.Contains(name)) Visit(name);
    }
}
