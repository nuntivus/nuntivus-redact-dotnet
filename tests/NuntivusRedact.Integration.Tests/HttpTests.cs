using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NuntivusRedact.AspNetCore;
using NuntivusRedact.Core;
using NuntivusRedact.Extensions.DependencyInjection;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Strategies;
using Xunit;

namespace NuntivusRedact.Integration.Tests;

public sealed class HttpTests
{
    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build())
            .AddDetector<DependencyInjectionTests.Detector>().AddDocumentProcessor<TextDocumentProcessor>().AddDocumentProcessor<JsonDocumentProcessor>();
        services.AddNuntivusRedactHttp(new() { MaximumRequestBytes = 32, MaximumResponseBytes = 64 });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    private static DefaultHttpContext Context(IServiceProvider provider, string format, string text, long? contentLength = null)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "POST";
        context.Request.RouteValues["format"] = format;
        context.Request.ContentType = "application/octet-stream";
        context.Request.ContentLength = contentLength;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(text));
        context.Response.Body = new MemoryStream();
        return context;
    }
    [Fact]
    public async Task SupportedBodyIsStagedAndReturnedWithSafeHeaders()
    {
        using var provider = Provider();
        var context = Context(provider, "text", "abc");
        await provider.GetRequiredService<NuntivusRedactHttpHandler>().HandleAsync(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("[REDACTED]bc", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
    }
    [Theory]
    [InlineData("pdf", "synthetic private data", null, 415)]
    [InlineData("text", "synthetic private data", 33L, 413)]
    [InlineData("text", "123456789012345678901234567890123", null, 413)]
    public async Task UnsupportedAndOversizedRequestsHaveSanitizedErrors(string format, string text, long? contentLength, int expected)
    {
        using var provider = Provider();
        var context = Context(provider, format, text, contentLength);
        await provider.GetRequiredService<NuntivusRedactHttpHandler>().HandleAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        var response = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.DoesNotContain(text, response);
        Assert.Contains("Redaction could not be completed", response);
    }
    [Fact]
    public async Task MalformedJsonReturnsSafe422InsteadOfDocumentOutput()
    {
        using var provider = Provider();
        var context = Context(provider, "json", "{private-malformed-data");
        await provider.GetRequiredService<NuntivusRedactHttpHandler>().HandleAsync(context);
        Assert.Equal(422, context.Response.StatusCode);
        var response = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.DoesNotContain("private-malformed-data", response);
        Assert.Contains("InvalidDocument", response);
        Assert.False(context.Response.Headers.ContainsKey("Content-Disposition"));
    }
    [Fact]
    public async Task CancellationAndMultipartAreNotProcessed()
    {
        using var provider = Provider();
        var context = Context(provider, "text", "private");
        context.RequestAborted = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<NuntivusRedactHttpHandler>().HandleAsync(context));
        Assert.Equal(0, context.Response.Body.Length);
        context = Context(provider, "text", "private");
        context.Request.ContentType = "multipart/form-data; boundary=example";
        await provider.GetRequiredService<NuntivusRedactHttpHandler>().HandleAsync(context);
        Assert.Equal(415, context.Response.StatusCode);
    }
}
