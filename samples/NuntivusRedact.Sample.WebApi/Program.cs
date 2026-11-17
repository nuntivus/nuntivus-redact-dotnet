using NuntivusRedact;
using NuntivusRedact.AspNetCore;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Extensions.DependencyInjection;
using NuntivusRedact.Formats.Csv;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Formats.Text;
using NuntivusRedact.Formats.Xml;
using NuntivusRedact.Strategies;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 4_000_000);
builder.Services.AddNuntivusRedact(new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build())
    .AddDocumentProcessor<TextDocumentProcessor>().AddDocumentProcessor<CsvDocumentProcessor>()
    .AddDocumentProcessor<JsonDocumentProcessor>().AddDocumentProcessor<XmlDocumentProcessor>();
foreach (var detector in PersonalDataDetectors.CreateDefault()) builder.Services.AddSingleton<ISensitiveDataDetector>(detector);
builder.Services.AddNuntivusRedactHttp();
var app = builder.Build();
app.MapNuntivusRedact();
app.MapGet("/openapi/v1.json", () => Results.File(Path.Combine(app.Environment.ContentRootPath, "openapi.json"), "application/json"));
app.Run();
