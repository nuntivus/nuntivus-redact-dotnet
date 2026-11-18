using System.Text;
using NuntivusRedact.Core;
using Xunit;

namespace NuntivusRedact.Core.Tests;

public sealed class EditTests
{
    private sealed class Detector : ISensitiveDataDetector
    {
        public int Calls { get; private set; }
        public string Id => "fixture";
        public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyList<Detection>>([
                new(new(2, 2), "a", 1, Id), new(new(5, 1), "a", 1, Id)]);
        }
    }
    private sealed class Strategy : IRedactionStrategy
    {
        public int Calls { get; private set; }
        public string Redact(ReadOnlySpan<char> source, Detection detection) => ++Calls == 1 ? "LONG" : "";
    }

    [Fact]
    public async Task EditsReconstructOutputWithOriginalOffsetsAndSingleExecution()
    {
        var detector = new Detector();
        var strategy = new Strategy();
        IEditAwareTextRedactor engine = new RedactionEngine([new(detector)], new RedactionPolicyBuilder(strategy).Build());
        var result = await engine.RedactWithEditsAsync("😀abcd!");
        Assert.Equal("😀LONGc!", result.Text);
        Assert.Equal(1, detector.Calls);
        Assert.Equal(2, strategy.Calls);
        var text = new StringBuilder("😀abcd!");
        foreach (var edit in result.Edits.Reverse())
            text.Remove(edit.Detection.Span.Start, edit.Detection.Span.Length).Insert(edit.Detection.Span.Start, edit.Replacement);
        Assert.Equal(result.Text, text.ToString());
        Assert.Equal(new[] { 2, 5 }, result.Edits.Select(edit => edit.Detection.Span.Start));
    }

    [Fact]
    public async Task OldTextInterfaceKeepsIdenticalSemantics()
    {
        var old = new RedactionEngine([new(new Detector())], new RedactionPolicyBuilder(new Strategy()).Build());
        var modern = new RedactionEngine([new(new Detector())], new RedactionPolicyBuilder(new Strategy()).Build());
        var legacy = await ((ITextRedactor)old).RedactAsync("😀abcd!");
        var edits = await modern.RedactWithEditsAsync("😀abcd!");
        Assert.Equal(legacy.Text, edits.Text);
        Assert.Equal(legacy.Detections, edits.Edits.Select(edit => edit.Detection));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await modern.RedactWithEditsAsync("😀abcd!", new(true)));
    }

    [Fact]
    public void ResultsSnapshotEditsAndDoNotPrintSensitiveReplacements()
    {
        var edit = new RedactionEdit(new(new(0, 1), "a", 1, "fixture"), "PRIVATE");
        var list = new List<RedactionEdit> { edit };
        var result = new EditRedactionResult("PRIVATE", list);
        list.Clear();
        Assert.Single(result.Edits);
        Assert.DoesNotContain("PRIVATE", edit.ToString());
        Assert.DoesNotContain("PRIVATE", result.ToString());
        Assert.Throws<ArgumentException>(() => new EditRedactionResult("", [edit, edit]));
        Assert.Throws<ArgumentException>(() => new EditRedactionResult("", [new(new(new(2, 1), "a", 1, "fixture"), ""), edit]));
    }
}
