using CheckerBase.App;
using CheckerBase.Core.Configuration;
using CheckerBase.Core.Engine;
using CheckerBase.Core.Proxies;

var options = new CheckerOptions
{
    DegreeOfParallelism = Environment.ProcessorCount,
    MaxRetries = 3
};

var outputOptions = new OutputOptions
{
    SuccessPath = "output/success.txt",
    FailedPath = "output/failed.txt",
    IgnoredPath = null // Don't write ignored
};

ProxyRotator? proxyRotator = null;
if (File.Exists("proxies.txt"))
{
    var loadResult = await ProxyLoader.LoadFromFileAsync("proxies.txt");
    proxyRotator = loadResult.Rotator;

    Console.WriteLine($"Loaded {loadResult.SuccessCount} proxies");
    if (loadResult.FailedCount > 0)
        Console.WriteLine($"Warning: {loadResult.FailedCount} lines failed to parse");
}

// Create checker and engine
var checker = new ExampleChecker();
var engine = new CheckerEngine<ComboEntry, CheckResult, HttpClient>(
    checker, options, outputOptions, proxyRotator);

// Handle Ctrl+C
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nCancelling...");
    engine.Cancel();
};

// Start metrics display task
var displayCts = new CancellationTokenSource();
var metricsTask = Task.Run(async () =>
{
    try
    {
        while (!displayCts.Token.IsCancellationRequested)
        {
            await Task.Delay(1000, displayCts.Token);
            var m = engine.Metrics.GetSnapshot();
            var eta = m.ETA?.ToString(@"hh\:mm\:ss") ?? "--:--:--";

            Console.Write($"\r[{m.ElapsedTime:hh\\:mm\\:ss}] " +
                         $"Progress: {m.ProgressPercent:F1}% | " +
                         $"Lines: {m.ProcessedLines} | " +
                         $"Success: {m.SuccessCount} | Failed: {m.FailedCount} | " +
                         $"CPM: {m.CPM:F0} | ETA: {eta}   ");
        }
    }
    catch (OperationCanceledException)
    {
        // Expected
    }
});

// Run
const string inputPath = "input.txt";

if (!File.Exists(inputPath))
{
    Console.WriteLine($"Error: Input file '{inputPath}' not found.");
    return 1;
}

Console.WriteLine($"Starting with {options.DegreeOfParallelism} workers...");
Console.WriteLine();

try
{
    await engine.RunAsync(inputPath);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nCancelled by user.");
}
finally
{
    await displayCts.CancelAsync();
    await metricsTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
}

Console.WriteLine();
Console.WriteLine();

var final = engine.Metrics.GetSnapshot();
Console.WriteLine("=== Final Results ===");
Console.WriteLine($"Total lines: {final.ProcessedLines}");
Console.WriteLine($"Success: {final.SuccessCount}");
Console.WriteLine($"Failed: {final.FailedCount}");
Console.WriteLine($"Ignored: {final.IgnoredCount}");
Console.WriteLine($"Retries: {final.RetryCount}");
Console.WriteLine($"Elapsed: {final.ElapsedTime:hh\\:mm\\:ss}");

return 0;