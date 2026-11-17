using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuntivusRedact.Core;
using NuntivusRedact.Extensions.DependencyInjection;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Integration.Tests;

public sealed class DependencyInjectionTests
{
    public sealed class Detector : ISensitiveDataDetector
    {
        public string Id => "test";
        public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Detection>>(context.Text.Length == 0 ? [] : [new(new(0, 1), "test", 1, Id)]);
    }
    public sealed class Strategy : IRedactionStrategy
    {
        public string Redact(ReadOnlySpan<char> source, Detection detection) => "X";
    }
    [Fact]
    public async Task ExplicitRegistrationMatchesStandaloneAndIsIdempotent()
    {
        var services = new ServiceCollection();
        var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build();
        services.AddNuntivusRedact(policy).AddDetector<Detector>().AddDetector<Detector>().AddStrategy<Strategy>().AddDocumentProcessor<TextDocumentProcessor>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var engine = provider.GetRequiredService<ITextRedactor>();
        Assert.Single(provider.GetServices<ISensitiveDataDetector>());
        Assert.Single(provider.GetServices<IRedactionStrategy>());
        Assert.Single(provider.GetServices<IDocumentProcessor>());
        Assert.Same(engine, provider.GetRequiredService<ITextRedactor>());
        var standalone = new RedactionEngine([new(new Detector())], policy);
        Assert.Equal((await standalone.RedactAsync("abc")).Text, (await engine.RedactAsync("abc")).Text);
    }
    [Fact]
    public void MissingOptionsAndNonSingletonExtensionsAreRejected()
    {
        var services = new ServiceCollection();
        services.AddNuntivusRedact(_ => { });
        using (var provider = services.BuildServiceProvider())
            Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ITextRedactor>());
        services.AddScoped<ISensitiveDataDetector, Detector>();
        Assert.Throws<InvalidOperationException>(() => ServiceCollectionExtensions.ValidateLifetimes(services));
    }
    public sealed class ScopedDependency;
    public sealed class CaptiveDetector(ScopedDependency dependency) : ISensitiveDataDetector
    {
        public string Id => dependency.GetType().Name;
        public ValueTask<IReadOnlyList<Detection>> DetectAsync(DetectionContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Detection>>([]);
    }
    [Fact]
    public void CaptiveScopedDependenciesAreRejectedByContainerValidation()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build()).AddDetector<CaptiveDetector>();
        Assert.Throws<AggregateException>(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
    }
}
