#r "nuget: Lestaly, 0.57.0"
#nullable enable
using System.Net.Http;
using System.Threading;
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(async () =>
{
    Console.WriteLine("Restart service");
    var composeFile = ThisSource.RelativeFile("./docker/docker-compose.yml");
    await "docker".args("compose", "--file", composeFile.FullName, "down", "--remove-orphans", "--volumes");
    await "docker".args("compose", "--file", composeFile.FullName, "up", "-d", "--wait").result().success();

    Console.WriteLine("completed.");
});
