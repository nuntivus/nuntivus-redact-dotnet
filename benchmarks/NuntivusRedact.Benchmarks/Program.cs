using BenchmarkDotNet.Running;
using NuntivusRedact.Benchmarks;

if (args is ["--verify-fixtures"])
{
    foreach (var scenario in Enum.GetValues<TextScenario>())
    {
        var benchmark = new TextBenchmarks { Scenario = scenario };
        benchmark.Setup();
        var result = await benchmark.Redact();
        var fixture = SyntheticFixtures.Text(scenario);
        if (result.Text != fixture.Expected || result.Detections.Count != fixture.ExpectedDetections)
            throw new InvalidOperationException("Repeated synthetic text verification failed.");
        Console.WriteLine($"Text {scenario}: {fixture.Input.Length} UTF-16 characters, {result.Detections.Count} accepted detections; verified.");
    }
    foreach (string format in new[] { "text", "json", "xml" })
    {
        foreach (int records in new[] { 8, 128 })
        {
            var benchmark = new DocumentBenchmarks { Format = format, Records = records };
            benchmark.Setup();
            if (await benchmark.Process() != records)
                throw new InvalidOperationException("Repeated synthetic document verification failed.");
            var fixture = SyntheticFixtures.Document(format, records);
            Console.WriteLine($"Document {format}/{records}: {fixture.Input.Length} UTF-8 bytes, {records} accepted detections; verified.");
        }
    }
    Console.WriteLine("All 10 deterministic fixtures verified twice; no source or output content logged.");
}
else
{
    BenchmarkSwitcher.FromAssembly(typeof(TextBenchmarks).Assembly).Run(args);
}
