using NuntivusRedact;
using NuntivusRedact.Core;
using NuntivusRedact.Strategies;

var engine = new RedactionEngine([new(new TicketDetector())], new RedactionPolicyBuilder(new CategoryReplacementStrategy()).Build());
var result = await engine.RedactAsync("Reference TICKET-1234 is private.");
if (result.Text != "Reference [ticket] is private.") throw new InvalidOperationException("Sample verification failed.");
Console.WriteLine($"Custom detector processed synthetic text; replacements: {result.Detections.Count}.");

internal sealed class TicketDetector : ISensitiveDataDetector
{
    public string Id => "ticket";
    public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Text.Length > 1_000_000) throw new RedactionException(RedactionError.LimitExceeded);
        var matches = new List<Detection>();
        for (int i = 0; i <= context.Text.Length - 11; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.Text.AsSpan(i, 7).SequenceEqual("TICKET-")) continue;
            if (!context.Text.AsSpan(i + 7, 4).ContainsAnyExceptInRange('0', '9'))
            {
                if (matches.Count == 1000) throw new RedactionException(RedactionError.LimitExceeded);
                matches.Add(new(new(i, 11), "ticket", 1, Id));
                i += 10;
            }
        }
        return ValueTask.FromResult<IReadOnlyList<Detection>>(matches);
    }
}
