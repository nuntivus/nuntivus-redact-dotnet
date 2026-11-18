using Microsoft.Extensions.DependencyInjection;
using NuntivusRedact.Core;
using NuntivusRedact.Extensions.DependencyInjection;
using NuntivusRedact.Formats.Office;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Integration.Tests;

public sealed class OfficeRegistrationTests
{
    [Fact]
    public void OfficeRegistrationIsExplicitIdempotentAndSharesTheEngine()
    {
        var services = new ServiceCollection();
        services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build())
            .AddDocumentProcessor<DocxDocumentProcessor>().AddDocumentProcessor<DocxDocumentProcessor>().AddDocumentProcessor<XlsxDocumentProcessor>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Same(provider.GetRequiredService<ITextRedactor>(), provider.GetRequiredService<IEditAwareTextRedactor>());
        Assert.Equal(new[] { "docx", "xlsx" }, provider.GetServices<IDocumentProcessor>().Select(p => p.Format));
    }

    [Fact]
    public void ReferencingThePackageDoesNotRegisterOfficeImplicitly()
    {
        var services = new ServiceCollection();
        services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build());
        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IDocumentProcessor>());
    }

    [Fact]
    public void CustomLegacyRedactorIsNotSilentlyReplacedToEnableOffice()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITextRedactor, LegacyRedactor>();
        services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build()).AddDocumentProcessor<DocxDocumentProcessor>();
        using var provider = services.BuildServiceProvider();
        Assert.IsType<LegacyRedactor>(provider.GetRequiredService<ITextRedactor>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IEditAwareTextRedactor>());
        Assert.Throws<InvalidOperationException>(() => provider.GetServices<IDocumentProcessor>().ToArray());
    }

    private sealed class LegacyRedactor : ITextRedactor
    {
        public ValueTask<RedactionResult> RedactAsync(string text, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RedactionResult(text, []));
    }
}
