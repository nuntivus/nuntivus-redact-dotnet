using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NuntivusRedact.Core;

namespace NuntivusRedact.Extensions.DependencyInjection;

/// <summary>Mutable registration options, validated when the singleton engine is resolved.</summary>
public sealed class NuntivusRedactOptions
{
    /// <summary>Gets or sets the immutable policy required to resolve the engine.</summary>
    public RedactionPolicy? Policy { get; set; }
}

/// <summary>Explicit singleton extension registration. Implementations and their dependencies must be thread safe.</summary>
public sealed class NuntivusRedactBuilder
{
    internal NuntivusRedactBuilder(IServiceCollection services) => Services = services;
    /// <summary>Gets the underlying registration collection.</summary>
    public IServiceCollection Services { get; }
    /// <summary>Registers a detector once as a singleton; constructor injection is supported.</summary>
    public NuntivusRedactBuilder AddDetector<T>() where T : class, ISensitiveDataDetector
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISensitiveDataDetector, T>());
        return this;
    }
    /// <summary>Registers an optional strategy extension once as a singleton.</summary>
    public NuntivusRedactBuilder AddStrategy<T>() where T : class, IRedactionStrategy
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRedactionStrategy, T>());
        return this;
    }
    /// <summary>Registers a document processor once as a singleton.</summary>
    public NuntivusRedactBuilder AddDocumentProcessor<T>() where T : class, IDocumentProcessor
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentProcessor, T>());
        return this;
    }
}

/// <summary>Optional Microsoft DI composition, independent of standalone Core usage.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers a strict singleton engine with validated options. No detectors are implicitly enabled.</summary>
    public static NuntivusRedactBuilder AddNuntivusRedact(this IServiceCollection services, Action<NuntivusRedactOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<NuntivusRedactOptions>().Configure(configure).Validate(o => o.Policy is not null, "An immutable redaction policy is required.").ValidateOnStart();
        services.TryAddSingleton<ITextRedactor>(provider =>
        {
            ValidateLifetimes(services);
            var policy = provider.GetRequiredService<IOptions<NuntivusRedactOptions>>().Value.Policy!;
            return new RedactionEngine(provider.GetServices<ISensitiveDataDetector>().Select(d => new DetectorRegistration(d)), policy);
        });
        services.TryAddSingleton<IEditAwareTextRedactor>(provider => provider.GetRequiredService<ITextRedactor>() as IEditAwareTextRedactor
            ?? throw new InvalidOperationException("The configured text redactor does not support original-coordinate edits."));
        return new NuntivusRedactBuilder(services);
    }
    /// <summary>Registers a strict engine using an already validated immutable policy.</summary>
    public static NuntivusRedactBuilder AddNuntivusRedact(this IServiceCollection services, RedactionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return services.AddNuntivusRedact(options => options.Policy = policy);
    }
    /// <summary>Rejects non-singleton direct extension registrations. Build the container with ValidateScopes and ValidateOnBuild for dependency-graph checks.</summary>
    public static void ValidateLifetimes(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] contracts = [typeof(ISensitiveDataDetector), typeof(IRedactionStrategy), typeof(IDocumentProcessor), typeof(ITextRedactor), typeof(IEditAwareTextRedactor)];
        if (services.Any(s => contracts.Contains(s.ServiceType) && s.Lifetime != ServiceLifetime.Singleton))
            throw new InvalidOperationException("Redaction extensions must use singleton lifetimes.");
    }
}
