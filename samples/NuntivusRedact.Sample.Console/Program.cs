using System.Text;
using NuntivusRedact.Core;
using NuntivusRedact.Detectors.PersonalData;
using NuntivusRedact.Formats.Json;
using NuntivusRedact.Strategies;

var policy = new RedactionPolicyBuilder(new FixedReplacementStrategy()).Build();
var engine = new RedactionEngine(PersonalDataDetectors.CreateDefault().Select(d => new DetectorRegistration(d)), policy);
using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"contact\":\"alice@example.com\",\"count\":42}"));
using var output = new MemoryStream();
var result = await new JsonDocumentProcessor(engine).ProcessAsync(input, output);
if (result.Detections.Count != 1 || Encoding.UTF8.GetString(output.ToArray()).Contains("alice", StringComparison.Ordinal))
    throw new InvalidOperationException("Sample verification failed.");
System.Console.WriteLine($"Processed synthetic JSON; replacements: {result.Detections.Count}. Complete discovery is not guaranteed.");
