using System.Reflection;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Detectors.Regex;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Architecture.Tests;

public sealed class DependencyTests
{
    [Fact]
    public void NlpContractsAndAdapterDoNotReferenceInferenceFrameworks()
    {
        foreach (var assembly in new[] { typeof(Nlp.IEntityRecognitionProvider).Assembly, typeof(Detectors.Nlp.NlpDetector).Assembly,
            typeof(RedactionEngine).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("Microsoft.ML.", StringComparison.Ordinal));
        Assert.Contains(typeof(Nlp.Onnx.OnnxEntityRecognitionProvider).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Microsoft.ML.OnnxRuntime");
    }

    [Fact]
    public void OfficeHelpersUseInterfacesAndInstanceImplementations()
    {
        var assembly = typeof(OfficeLimits).Assembly;
        Assert.DoesNotContain(assembly.GetTypes(), type => type.IsClass && type.IsAbstract && type.IsSealed &&
            !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false));
        foreach (var name in new[] { "OfficePackage", "OfficeContent", "ExcelContent", "OfficeString", "OfficeDrawing" })
        {
            var implementation = assembly.GetType($"NuntivusRedact.Formats.Office.{name}", true)!;
            var contract = assembly.GetType($"NuntivusRedact.Formats.Office.I{name}", true)!;
            Assert.True(contract.IsInterface);
            Assert.True(contract.IsAssignableFrom(implementation));
            Assert.True(implementation.IsSealed);
            Assert.DoesNotContain(implementation.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), method => !method.IsSpecialName);
            Assert.All(contract.GetMethods(), method => Assert.False(method.IsStatic));
        }
        foreach (var processor in new[] { typeof(DocxDocumentProcessor), typeof(XlsxDocumentProcessor) })
            Assert.Contains(processor.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field => field.FieldType.Name == "IOfficePackage");
    }

    [Fact]
    public void ProductAssembliesDependInwardWithoutCyclesOrDiInCore()
    {
        Assembly[] assemblies = [typeof(Detection).Assembly, typeof(RedactionEngine).Assembly,
            typeof(PersonalDataDetectors).Assembly, typeof(RegexDetector).Assembly,
            typeof(FixedReplacementStrategy).Assembly, typeof(JsonDocumentProcessor).Assembly,
            typeof(TextDocumentProcessor).Assembly, typeof(OfficeLimits).Assembly,
            typeof(Ocr.IOcrProvider).Assembly, typeof(Ocr.Tesseract.TesseractOcrProvider).Assembly,
            typeof(Formats.Images.RasterImageRedactor).Assembly, typeof(Nlp.IEntityRecognitionProvider).Assembly,
            typeof(Detectors.Nlp.NlpDetector).Assembly, typeof(Nlp.Onnx.OnnxEntityRecognitionProvider).Assembly,
            typeof(Pdf.IPdfBackend).Assembly, typeof(Pdf.PdfiumCore.PdfiumBackend).Assembly,
            typeof(Pdf.PdfPig.PdfPigStructuralInspector).Assembly, typeof(Pdf.Experimental.ExperimentalPdfRedactor).Assembly];
        var allowed = new Dictionary<string, string[]>
        {
            ["NuntivusRedact.Abstractions"] = [],
            ["NuntivusRedact.Core"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Nlp.Abstractions"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Detectors.Nlp"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Nlp.Abstractions"],
            ["NuntivusRedact.Nlp.Onnx"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Nlp.Abstractions"],
            ["NuntivusRedact.Detectors.Regex"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Detectors.PersonalData"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Detectors.Regex"],
            ["NuntivusRedact.Strategies"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Formats.Text"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Formats.Office"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Ocr.Abstractions"],
            ["NuntivusRedact.Ocr.Abstractions"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Ocr.Tesseract"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Ocr.Abstractions"],
            ["NuntivusRedact.Formats.Images"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Ocr.Abstractions"],
            ["NuntivusRedact.Formats.Json"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Formats.Text"],
            ["NuntivusRedact.Pdf.Abstractions"] = ["NuntivusRedact.Abstractions"],
            ["NuntivusRedact.Pdf.PdfiumCore"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Pdf.Abstractions"],
            ["NuntivusRedact.Pdf.PdfPig"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Pdf.Abstractions"],
            ["NuntivusRedact.Pdf.Experimental"] = ["NuntivusRedact.Abstractions", "NuntivusRedact.Pdf.Abstractions"]
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

    [Fact]
    public void PdfContractsAndCoreDoNotReferenceNativeSdk()
    {
        foreach (var assembly in new[] { typeof(Pdf.IPdfBackend).Assembly, typeof(RedactionEngine).Assembly,
            typeof(Pdf.Experimental.ExperimentalPdfRedactor).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "PDFiumCore" ||
                reference.Name!.StartsWith("UglyToad.PdfPig", StringComparison.Ordinal));
        Assert.Contains(typeof(Pdf.PdfiumCore.PdfiumBackend).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "PDFiumCore");
        Assert.DoesNotContain(typeof(Pdf.PdfPig.PdfPigStructuralInspector).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "PDFiumCore" || reference.Name == "NuntivusRedact.Pdf.PdfiumCore");
        Assert.DoesNotContain(typeof(Pdf.PdfiumCore.PdfiumBackend).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.StartsWith("UglyToad.PdfPig", StringComparison.Ordinal) || reference.Name == "NuntivusRedact.Pdf.PdfPig");
    }
}
