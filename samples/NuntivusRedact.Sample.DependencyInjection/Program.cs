using Microsoft.Extensions.DependencyInjection;
using NuntivusRedact;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.Regex;
using NuntivusRedact.Extensions.DependencyInjection;
using NuntivusRedact.Strategies;

var services = new ServiceCollection();
services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build()).AddDetector<ProjectKeywordDetector>();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
var result = await provider.GetRequiredService<ITextRedactor>().RedactAsync("Project codename is ORCHID.");
if (result.Text != "Project codename is [REDACTED].") throw new InvalidOperationException("Sample verification failed.");
Console.WriteLine($"Processed synthetic text; replacements: {result.Detections.Count}.");

internal sealed class ProjectKeywordDetector : ISensitiveDataDetector
{
    private readonly DictionaryDetector detector = new("project-code", "project", ["ORCHID"]);
    public string Id => detector.Id;
    public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) => detector.DetectAsync(context, cancellationToken);
}
