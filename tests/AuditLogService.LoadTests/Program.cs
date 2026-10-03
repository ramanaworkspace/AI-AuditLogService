using System.Globalization;
using AuditLogService.LoadTests;

if (args.Length != 5
    || !Uri.TryCreate(args[0], UriKind.Absolute, out var url)
    || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
    || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var clients) || clients < 50
    || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var writes) || writes < 1
    || !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var timeout) || timeout < 1
    || (long)clients * writes > int.MaxValue)
{
    Console.Error.WriteLine("Usage: <api-url> <clients>=50+ <writes-per-client>=1+ <timeout-seconds>=1+ <output-directory>");
    return 2;
}

var result = await LoadTestRunner.RunAsync(url, clients, writes, timeout);
Directory.CreateDirectory(args[4]);
var prefix = Path.Combine(args[4], $"load-{result.RunId}");
await File.WriteAllBytesAsync(prefix + ".json", result.ToJson());
var summary = result.ToMarkdown();
await File.WriteAllTextAsync(prefix + ".md", summary);
Console.WriteLine(summary);
Console.WriteLine($"Results: {Path.GetFullPath(prefix)}.json and .md");
return result.IsSuccessful ? 0 : 1;
