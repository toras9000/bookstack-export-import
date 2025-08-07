#r "nuget: Kokuban, 0.2.0"
#r "nuget: Lestaly.General, 0.102.0"
#nullable enable
using System.Text.RegularExpressions;
using Kokuban;
using Lestaly;

var settings = new
{
    // Search directory for script files
    TargetDir = ThisSource.RelativeDirectory("../"),

    // Packages and versions to be unified and updated
    Packages = new PackageVersion[]
    {
        new("BookStackApiClient",         "25.7.0-lib.1"  ),
        new("Lestaly.General",            "0.102.0"       ),
        new("SkiaSharp",                  "3.119.0"       ),
        new("Faker.Net",                  "2.0.163"       ),
        new("Bogus",                      "35.6.3"        ),
        new("Kokuban",                    "0.2.0"         ),
        new("R3",                         "1.3.0"         ),
        new("Dapper",                     "2.1.66"        ),
        new("MySqlConnector",             "2.4.0"         ),
        new("BCrypt.Net-Next",            "4.0.3"         ),
        new("NuGet.Protocol",             "6.14.0"        ),
    },
};

return await Paved.ProceedAsync(async () =>
{
    // Detection regular expression for package reference directives
    var detector = new Regex(@"^\s*#\s*r\s+""\s*nuget\s*:\s*(?<package>[a-zA-Z0-9_\-\.]+)(?:,| )\s*(?<version>.+)\s*""");

    // Dictionary of packages to be updated
    var versions = settings.Packages.ToDictionary(p => p.Name);

    // Search for scripts under the target directory
    foreach (var file in settings.TargetDir.EnumerateFiles("*.csx", SearchOption.AllDirectories))
    {
        WriteLine($"File: {file.RelativePathFrom(settings.TargetDir, ignoreCase: true)}");

        // Read file contents
        var lines = await file.ReadAllLinesAsync();

        // Attempt to update package references
        var detected = false;
        var updated = false;
        for (var i = 0; i < lines.Length; i++)
        {
            // Detecting Package Reference Directives
            var line = lines[i];
            var match = detector.Match(line);
            if (!match.Success) continue;
            detected = true;

            // Determine if the package is eligible for renewal
            var pkgName = match.Groups["package"].Value;
            if (!versions.TryGetValue(pkgName, out var package))
            {
                WriteLine(Chalk.BrightYellow[$"  Skip: {pkgName} - Not update target"]);
                continue;
            }

            // Parse the version number.
            if (!SemanticVersion.TryParse(match.Groups["version"].Value, out var pkgVer))
            {
                WriteLine(Chalk.Yellow[$"  Skip: Unable to recognize version number"]);
                continue;
            }

            // Determine if the package version needs to be updated.
            if (pkgVer == package.SemanticVersion)
            {
                WriteLine(Chalk.Gray[$"  Skip: {pkgName} - Already in version"]);
                continue;
            }

            // Create a replacement line for the reference directive
            var newLine = @$"#r ""nuget: {pkgName}, {package.Version}""";
            lines[i] = newLine;

            // set a flag that there is an update
            updated = true;
            WriteLine(Chalk.Green[$"  Update: {pkgName} {pkgVer.Original} -> {package.Version}"]);
        }

        // Write back to file if updates are needed
        if (updated)
        {
            await file.WriteAllLinesAsync(lines);
        }
        else if (!detected)
        {
            WriteLine(Chalk.Gray[$"  Directive not found"]);
        }
    }

});

// Package version information data type
record PackageVersion(string Name, string Version)
{
    public SemanticVersion SemanticVersion { get; } = SemanticVersion.Parse(Version);
}
