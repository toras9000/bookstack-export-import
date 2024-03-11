#r "nuget: Lestaly, 0.56.0"
#nullable enable
using System.Net.Http;
using System.Threading;
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(async () =>
{
    Console.WriteLine("Restart service");
    var composeFile = ThisSource.RelativeFile("./docker/docker-compose.yml");
    var bindFile = ThisSource.RelativeFile("./docker/volume-bind.yml");
    await "docker".args("compose", "--file", composeFile.FullName, "down", "--remove-orphans", "--volumes");
    await "docker".args("compose", "--file", composeFile.FullName, "--file", bindFile.FullName, "up", "-d", "--wait").result().success();

    Console.WriteLine("completed.");
});
