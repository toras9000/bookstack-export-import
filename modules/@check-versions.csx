#r "nuget: NuGet.Protocol, 6.14.0"
#r "nuget: R3, 1.3.0"
#r "nuget: Lestaly.General, 0.102.0"
#nullable enable
using Lestaly;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using R3;

// This script requires a isolated assembly context.
// `dotnet script --isolated-load-context ./@check-versions.csx`

var settings = new
{
    // overwrite source script
    OverwriteUpdate = true,
};

return await Paved.ProceedAsync(async () =>
{
    // collect target packages info
    var sourceFile = ThisSource.RelativeFile("@update-packages.csx");
    var sourceLines = await sourceFile.ReadAllLinesAsync();
    var beginLine = sourceLines.Index().SkipWhile(e => !e.Item.IsMatch(@"Packages\s*=\s*new\s+PackageVersion\s*\[\s*\]")).Skip(1).First().Index;
    var endLine = sourceLines.Index().Skip(beginLine + 1).SkipWhile(e => !e.Item.TrimStart().StartsWith("}")).First().Index;

    // package sources
    var config = NuGet.Configuration.Settings.LoadDefaultSettings(default);
    var sources = NuGet.Configuration.PackageSourceProvider.LoadPackageSources(config).ToArray();
    var seachers = await sources.ToObservable()
        .SelectAwait(async (s, c) => await Repository.Factory.GetCoreV3(s).GetResourceAsync<PackageMetadataResource>(c))
        .ToArrayAsync();

    // find context
    var cache = new SourceCacheContext();
    var logger = NullLogger.Instance;

    // check versions
    var editInfos = new List<(int index, string name, string version)>();
    foreach (var index in Enumerable.Range(beginLine, endLine - beginLine))
    {
        // check update target
        var line = sourceLines[index];
        var match = line.Match(@"^\s*new\s*\(""(?<name>.+?)""\s*,\s*""(?<version>.+?)""\s*\)");
        if (!match.Success) continue;
        var srcName = match.Groups["name"].Value;
        var srcVer = match.Groups["version"].Value;

        // parse current version
        if (!NuGetVersion.TryParse(srcVer, out var targetVer))
        {
            continue;
        }

        // get latest version
        var metadatas = await seachers.ToObservable()
            .SelectAwait(async (r, c) => await r.GetMetadataAsync(srcName, includePrerelease: targetVer.IsPrerelease, includeUnlisted: false, cache, logger, c))
            .SelectMany(vers => vers.ToObservable())
            .ToArrayAsync();
        var latest = metadatas.MaxBy(m => m.Identity.Version, VersionComparer.Default);
        editInfos.Add((index, srcName, (latest?.Identity.Version ?? targetVer).ToFullString()));
    }

    // print list
    WriteLine("Latest versions:");
    var nameWidth = editInfos.Max(v => v.name?.Length ?? 0);
    var verWidth = editInfos.Max(v => v.version?.Length ?? 0);
    foreach (var info in editInfos)
    {
        var namePad = "".PadRight(nameWidth - info.name.Length);
        var verPad = "".PadRight(verWidth - info.version.Length);
        var newLine = $"        new(\"{info.name}\",{namePad}   \"{info.version}\"{verPad}  ),";
        WriteLine(newLine);
        sourceLines[info.index] = newLine;
    }

    // overwrite script
    if (settings.OverwriteUpdate)
    {
        WriteLine("Overwrite script");
        await sourceFile.WriteAllLinesAsync(sourceLines);
    }
});
