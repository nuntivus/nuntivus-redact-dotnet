using System.Text;
using System.Text.Json;
using NuntivusRedact.Pdf.PdfPig;
using Xunit;

namespace NuntivusRedact.Pdf.Tests;

public sealed class PdfStructuralInspectorTests
{
    private readonly IPdfStructuralInspector inspector = new PdfPigStructuralInspector();

    [Fact]
    public async Task InventoriesCurrentObjectsWithoutSourceValuesOrAdmissionClaims()
    {
        var result = await inspector.InspectAsync(PdfFixtures.Create(hiddenContent: true), new PdfInspectionLimits());
        Assert.Equal(1, result.PageCount);
        Assert.Equal(8, result.ObjectCount);
        Assert.Equal(3, result.StreamCount);
        Assert.Equal(1, result.MetadataStreamCount);
        Assert.Equal(1, result.UnreferencedObjectCount);
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.Metadata));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.Images));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.OpaqueStreams));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.UnreferencedObjects));
        string serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(PdfFixtures.Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PdfFixtures.HiddenSecret, serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AcroForm", "<< /Fields [] >>", PdfStructuralFeatures.Forms)]
    [InlineData("Names", "<< /EmbeddedFiles << /Names [] >> >>", PdfStructuralFeatures.Attachments)]
    [InlineData("Names", "<< /Java#53cript << /Names [] >> >>", PdfStructuralFeatures.Actions)]
    [InlineData("OpenAction", "<< /S /JavaScript /JS (private synthetic script) >>", PdfStructuralFeatures.Actions)]
    [InlineData("OCProperties", "<< /OCGs [] /D << >> >>", PdfStructuralFeatures.OptionalContent)]
    [InlineData("AF", "[]", PdfStructuralFeatures.Attachments)]
    public async Task DetectsNestedAndEscapedFeatureNames(string key, string value, PdfStructuralFeatures feature)
    {
        var pdf = StructuralPdfFixtures.Create(catalogEntries: $"/{key} 5 0 R", additionalObjects: [value]);
        var result = await inspector.InspectAsync(pdf, new PdfInspectionLimits());
        Assert.True(result.Features.HasFlag(feature));
    }

    [Fact]
    public async Task FindsAnnotationAndSignatureObjects()
    {
        var pdf = StructuralPdfFixtures.Create(pageEntries: "/Annots [5 0 R]", additionalObjects:
            ["<< /Type /Annot /Subtype /Widget /Rect [0 0 20 20] /FT /Sig /V 6 0 R >>",
             "<< /Type /Sig /ByteRange [0 0 0 0] /Contents <0000> >>"]);
        var result = await inspector.InspectAsync(pdf, new PdfInspectionLimits());
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.Annotations));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.Forms));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.Signatures));
    }

    [Fact]
    public async Task DetectsFormObjectsMasksAndEmbeddedFontReferences()
    {
        var pdf = StructuralPdfFixtures.Create(additionalObjects:
            ["<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Length 0 >>\nstream\n\nendstream",
             "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask [0 0] /Length 1 >>\nstream\nx\nendstream",
             "<< /Type /FontDescriptor /FontFile2 8 0 R >>",
             "<< /Length 1 >>\nstream\nx\nendstream"]);
        var result = await inspector.InspectAsync(pdf, new PdfInspectionLimits());
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.FormXObjects));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.ImageMasks));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.EmbeddedFonts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavesMetadataStreamsOpaqueAndDoesNotSearchTheirBytes(bool compressed)
    {
        var result = await inspector.InspectAsync(StructuralPdfFixtures.Metadata(compressed), new PdfInspectionLimits());
        Assert.Equal(1, result.MetadataStreamCount);
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.OpaqueStreams));
        Assert.False(result.Features.HasFlag(PdfStructuralFeatures.Actions));
    }

    [Fact]
    public async Task ReportsClassicRevisionsWithoutCertifyingHistoricalRemoval()
    {
        var result = await inspector.InspectAsync(StructuralPdfFixtures.Incremental(), new PdfInspectionLimits());
        Assert.Equal(2, result.RevisionCount);
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.AdditionalRevisions));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.Metadata));
        Assert.Equal(0, result.UnreferencedObjectCount);
    }

    [Fact]
    public async Task DecodesBoundedCrossReferenceStreamsWithoutAdmissionClaims()
    {
        var result = await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(), new PdfInspectionLimits());
        Assert.Equal(1, result.PageCount);
        Assert.Equal(5, result.ObjectCount);
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.CrossReferenceStreams));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.DecodedStructuralStreams));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.OpaqueStreams));
    }

    [Fact]
    public async Task StructuralDecodingBudgetsFailClosedAndRecover()
    {
        var pdf = StructuralPdfFixtures.CompressedXref();
        PdfInspectionLimits[] limits = [new() { MaximumDecodedStructuralStreamBytes = 1 },
            new() { MaximumTotalDecodedStructuralStreamBytes = 1 }, new() { MaximumObjects = 1 }];
        foreach (var limit in limits)
        {
            var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(pdf, limit).AsTask());
            Assert.Equal(RedactionError.LimitExceeded, error.Error);
            Assert.Null(error.InnerException);
        }
        Assert.Equal(1, (await inspector.InspectAsync(pdf, new PdfInspectionLimits())).PageCount);
    }

    [Fact]
    public async Task ReferencedObjectStreamsRemainUnsupportedAndInspectorRecovers()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(objectStream: true), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Null(error.InnerException);
        Assert.Equal(1, (await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(), new PdfInspectionLimits())).PageCount);
    }

    [Fact]
    public async Task GuardedFlateObjectStreamsExposeCompressedCatalogObjects()
    {
        var result = await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(flateObjectStream: true), new PdfInspectionLimits());
        Assert.Equal(1, result.PageCount);
        Assert.Equal(5, result.ObjectCount);
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.ObjectStreams));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.DecodedStructuralStreams));
    }

    [Theory]
    [InlineData("1 1 ", 1, 4, 0)]
    [InlineData("1 0 ", 1, 1000, 0)]
    [InlineData("1 0 ", 2, 4, 0)]
    [InlineData("1 0 ", 1, 4, 1)]
    [InlineData("1 0 1 1 ", 2, 8, 0)]
    public async Task MalformedObjectStreamHeadersAndIndexesFailClosed(string header, int count, int first, int index)
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(flateObjectStream: true, objectHeader: header, objectCount: count,
                objectFirst: first, objectIndex: index), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task DeclaredObjectStreamCountIsBoundedBeforeHeaderParsingAndRecovers()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(flateObjectStream: true, objectCount: 100_000), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Equal(1, (await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(flateObjectStream: true), new PdfInspectionLimits())).PageCount);
    }

    [Theory]
    [InlineData("/DecodeParms << /Predictor 2 /Columns 7 >>")]
    [InlineData("/DecodeParms << /Predictor 12 /Columns 8 >>")]
    [InlineData("/DecodeParms << /Predictor 12 /Columns 7 /Colors 3 >>")]
    [InlineData("/DecodeParms << /Predictor 12 /Columns 7 /BitsPerComponent 16 >>")]
    [InlineData("/DecodeParms << /Predictor 12.5 /Columns 7 >>")]
    [InlineData("/DecodeParms << /Predictor 12 /Columns 7 /Unknown 1 >>")]
    public async Task PredictorParametersRemainExplicitlyUnsupported(string entries)
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(entries), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.UnsupportedFormat, error.Error);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public async Task BoundedPngPredictorsRecoverCrossReferenceRows(int predictor)
    {
        var result = await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(predictor: predictor), new PdfInspectionLimits());
        Assert.Equal(1, result.PageCount);
        Assert.Equal(5, result.ObjectCount);
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.DecodedStructuralStreams));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task InvalidPredictorRowsFailClosed(bool invalidTag, bool truncate)
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(predictor: 12, invalidTag: invalidTag, truncate: truncate), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task PredictorReconstructionChargesAggregateDecodedBudgetAndRecovers()
    {
        var pdf = StructuralPdfFixtures.CompressedXref(predictor: 12);
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(pdf,
            new PdfInspectionLimits { MaximumTotalDecodedStructuralStreamBytes = 60 }).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Equal(1, (await inspector.InspectAsync(pdf, new PdfInspectionLimits())).PageCount);
    }

    [Fact]
    public async Task SingleFilterParameterArraysAndNullParametersAreSupported()
    {
        var result = await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(
            entries: "/DecodeParms [<< /Predictor 12 /Columns 7 >>]", predictor: 12), new PdfInspectionLimits());
        Assert.Equal(1, result.PageCount);
        Assert.Equal(1, (await inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(
            entries: "/DecodeParms [null]"), new PdfInspectionLimits())).PageCount);
    }

    [Fact]
    public async Task FixedPredictorRejectsDifferentValidRowFilter()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(StructuralPdfFixtures.CompressedXref(
            entries: "/DecodeParms << /Predictor 12 /Columns 7 >>", predictor: 11), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task PredictorInspectionIsReusableAcrossConcurrentCalls()
    {
        var pdf = StructuralPdfFixtures.CompressedXref(predictor: 15);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            await inspector.InspectAsync(pdf, new PdfInspectionLimits()))));
        Assert.All(results, result => Assert.Equal(results[0], result));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TruncatedRowsAndCorruptFlateCannotProduceInventory(bool truncate, bool corrupt)
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(truncate: truncate, corrupt: corrupt), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task HighlyCompressedStructuralPayloadIsBoundedBeforeInventory()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(
            StructuralPdfFixtures.CompressedXref(bomb: true), new PdfInspectionLimits { MaximumDecodedStructuralStreamBytes = 1024 }).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task EnforcesInputReadOperationObjectTokenAndEncodedStreamBudgets()
    {
        var pdf = PdfFixtures.Create();
        PdfInspectionLimits[] limits = [
            new() { MaximumInputBytes = 1 },
            new() { MaximumReadBytes = 1 },
            new() { MaximumStreamOperations = 1 },
            new() { MaximumObjects = 1 },
            new() { MaximumTokens = 1 },
            new() { MaximumEncodedStreamBytes = 1 },
            new() { MaximumTotalEncodedStreamBytes = 1 }
        ];
        foreach (var limit in limits)
        {
            var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(pdf, limit).AsTask());
            Assert.Equal(RedactionError.LimitExceeded, error.Error);
        }
    }

    [Fact]
    public async Task EnforcesRevisionAndParserNestingLimits()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(StructuralPdfFixtures.Incremental(), new PdfInspectionLimits { MaximumRevisions = 1 }).AsTask());
        Assert.Equal(RedactionError.LimitExceeded, error.Error);
        var nested = StructuralPdfFixtures.Create(additionalObjects: [new string('[', 100) + "0" + new string(']', 100)]);
        error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(nested, new PdfInspectionLimits { MaximumDepth = 8 }).AsTask());
        Assert.Contains(error.Error, new[] { RedactionError.InvalidDocument, RedactionError.LimitExceeded });
    }

    [Fact]
    public async Task MissingReferencesFailClosed()
    {
        var pdf = StructuralPdfFixtures.Create(pageEntries: "/Annots [99 0 R]");
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(pdf, new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
    }

    [Fact]
    public async Task UnknownObjectTypesAreExplicitlyReported()
    {
        var pdf = StructuralPdfFixtures.Create(additionalObjects: ["<< /Type /UnrecognizedExtension >>"]);
        var result = await inspector.InspectAsync(pdf, new PdfInspectionLimits());
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.UnclassifiedObjects));
        Assert.True(result.Features.HasFlag(PdfStructuralFeatures.UnreferencedObjects));
    }

    [Fact]
    public async Task EncryptedOrMalformedEncryptionStructuresCannotReturnAnInventory()
    {
        var pdf = StructuralPdfFixtures.Create(additionalObjects: ["<< /Filter /Standard /V 4 /R 4 >>"], trailerEntries: "/Encrypt 5 0 R");
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(pdf, new PdfInspectionLimits()).AsTask());
        Assert.Contains(error.Error, new[] { RedactionError.InvalidDocument, RedactionError.UnsupportedFormat });
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task IncorrectCrossReferenceOffsetsCannotBeRepairedSilently()
    {
        var raw = Encoding.ASCII.GetString(StructuralPdfFixtures.Create());
        Assert.Contains("0000000009 00000 n ", raw, StringComparison.Ordinal);
        var modified = Encoding.ASCII.GetBytes(raw.Replace("0000000009 00000 n ", "0000000010 00000 n ", StringComparison.Ordinal));
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(modified, new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
    }

    [Fact]
    public async Task MalformedInputAndCancellationAreSanitizedAndRecoverable()
    {
        var error = await Assert.ThrowsAsync<RedactionException>(() => inspector.InspectAsync(Encoding.ASCII.GetBytes("%PDF-private-value malformed"), new PdfInspectionLimits()).AsTask());
        Assert.Equal(RedactionError.InvalidDocument, error.Error);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-value", error.Message, StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.InspectAsync(PdfFixtures.Create(), new PdfInspectionLimits(), cancellation.Token).AsTask());
        Assert.Equal(1, (await inspector.InspectAsync(PdfFixtures.Create(), new PdfInspectionLimits())).PageCount);
    }

    [Fact]
    public async Task ReusableInspectorProducesDeterministicConcurrentObservations()
    {
        var pdf = PdfFixtures.Create(hiddenContent: true);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () => await inspector.InspectAsync(pdf, new PdfInspectionLimits()))));
        Assert.All(results, result => Assert.Equal(results[0], result));
    }
}
