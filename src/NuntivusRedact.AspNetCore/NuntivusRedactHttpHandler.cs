using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NuntivusRedact.Formats.Text;

namespace NuntivusRedact.AspNetCore;

/// <summary>Immutable HTTP staging limits, independent of each processor's own limits.</summary>
public sealed record NuntivusRedactHttpOptions
{
    /// <summary>Gets the maximum request body size, including chunked uploads.</summary>
    public int MaximumRequestBytes { get; init; } = 4_000_000;
    /// <summary>Gets the maximum staged response size.</summary>
    public int MaximumResponseBytes { get; init; } = 8_000_000;
}

/// <summary>Optional bounded raw-body redaction endpoint. Authentication, TLS, rate limits and authorization belong to the host.</summary>
public sealed class NuntivusRedactHttpHandler
{
    private readonly DocumentProcessorRegistry registry;
    private readonly NuntivusRedactHttpOptions options;
    /// <summary>Creates a handler without request/content logging.</summary>
    public NuntivusRedactHttpHandler(DocumentProcessorRegistry registry, NuntivusRedactHttpOptions options)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.MaximumRequestBytes is < 1 or > 100_000_000 || options.MaximumResponseBytes is < 1 or > 100_000_000)
            throw new ArgumentException("HTTP limits are outside supported ranges.", nameof(options));
    }
    /// <summary>Handles POST raw bytes using the registered route format; never accepts multipart or falls back to text.</summary>
    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var token = context.RequestAborted;
        token.ThrowIfCancellationRequested();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        try
        {
            if (context.Request.ContentLength > options.MaximumRequestBytes) throw new RedactionException(RedactionError.LimitExceeded);
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = options.MaximumRequestBytes;
            if (!string.IsNullOrEmpty(context.Request.Headers.ContentEncoding)) throw new RedactionException(RedactionError.UnsupportedFormat);
            if (context.Request.ContentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true) throw new RedactionException(RedactionError.UnsupportedFormat);
            var format = context.Request.RouteValues["format"] as string;
            if (string.IsNullOrWhiteSpace(format)) throw new RedactionException(RedactionError.UnsupportedFormat);
            var processor = registry.Get(format);
            using var input = new LimitedMemoryStream(options.MaximumRequestBytes);
            await context.Request.Body.CopyToAsync(input, token).ConfigureAwait(false);
            input.Position = 0;
            using var output = new LimitedMemoryStream(options.MaximumResponseBytes);
            await processor.ProcessAsync(input, output, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = processor.Format switch
            {
                "text" => "text/plain; charset=utf-8",
                "csv" => "text/csv; charset=utf-8",
                "json" => "application/json",
                "xml" => "application/xml",
                _ => "application/octet-stream"
            };
            context.Response.Headers.ContentDisposition = "attachment; filename=redacted.bin";
            context.Response.ContentLength = output.Length;
            output.Position = 0;
            await output.CopyToAsync(context.Response.Body, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { context.Abort(); throw new OperationCanceledException(token); }
        catch (RedactionException error) { await WriteErrorAsync(context, error.Error).ConfigureAwait(false); }
        catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge) { await WriteErrorAsync(context, RedactionError.LimitExceeded).ConfigureAwait(false); }
        catch (Exception) { await WriteErrorAsync(context, RedactionError.ExtensionFailed).ConfigureAwait(false); }
    }
    private static Task WriteErrorAsync(HttpContext context, RedactionError error)
    {
        if (context.Response.HasStarted) { context.Abort(); return Task.CompletedTask; }
        context.Response.ContentLength = null;
        context.Response.Headers.Remove("Content-Disposition");
        int status = error switch
        {
            RedactionError.LimitExceeded => StatusCodes.Status413PayloadTooLarge,
            RedactionError.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            RedactionError.InvalidDocument or RedactionError.InvalidDetection => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Problem(statusCode: status, title: "Redaction could not be completed.", extensions: new Dictionary<string, object?> { ["code"] = error.ToString() }).ExecuteAsync(context);
    }
    private sealed class LimitedMemoryStream(int maximum) : MemoryStream
    {
        private void Check(int count) { if (count > maximum - Position) throw new RedactionException(RedactionError.LimitExceeded); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { Check(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { Check(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override void SetLength(long value) { if (value > maximum) throw new RedactionException(RedactionError.LimitExceeded); base.SetLength(value); }
    }
}

/// <summary>Explicit HTTP service and route registration.</summary>
public static class NuntivusRedactHttpExtensions
{
    /// <summary>Registers a singleton handler and registry over explicitly registered document processors.</summary>
    public static IServiceCollection AddNuntivusRedactHttp(this IServiceCollection services, NuntivusRedactHttpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(options ?? new NuntivusRedactHttpOptions());
        services.TryAddSingleton<DocumentProcessorRegistry>();
        services.TryAddSingleton<NuntivusRedactHttpHandler>();
        return services;
    }
    /// <summary>Maps a raw-body POST endpoint. Add host security before exposing outside a trusted local environment.</summary>
    public static RouteHandlerBuilder MapNuntivusRedact(this IEndpointRouteBuilder endpoints, string pattern = "/redact/{format}") =>
        endpoints.MapPost(pattern, (HttpContext context, NuntivusRedactHttpHandler handler) => handler.HandleAsync(context))
            .WithName("RedactDocument").WithTags("Redaction")
            .Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
}
