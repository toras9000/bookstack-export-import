#r "nuget: Lestaly, 0.64.0"
#nullable enable
using System.Net.Http;
using System.Threading;
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(config: c => c.AnyPause(), action: async () =>
{
    WriteLine("Restart service");
    var composeFile = ThisSource.RelativeFile("./docker/compose.yml");
    await "docker".args("compose", "--file", composeFile.FullName, "down", "--remove-orphans", "--volumes");
    await "docker".args("compose", "--file", composeFile.FullName, "up", "-d", "--wait").result().success();

    WriteLine();
    WriteLine("Container up completed.");
    WriteLine("Service URL");
    ConsoleWig.Write(" ").WriteLink("http://localhost:9971").NewLine();
    ConsoleWig.Write(" ").WriteLink("http://localhost:9972").NewLine();
    WriteLine();
});
