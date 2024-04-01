#r "nuget: Lestaly, 0.58.0"
#nullable enable
using System.Threading;
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(async () =>
{
    Console.WriteLine("Stop service");
    var composeFile = ThisSource.RelativeFile("./docker/docker-compose.yml");
    await "docker".args("compose", "--file", composeFile.FullName, "down", "--remove-orphans", "--volumes").silent();

    Console.WriteLine("Delete volumes");
    var volumesDir = ThisSource.RelativeDirectory("./docker/volumes");
    volumesDir.DeleteRecurse();

    Console.WriteLine("completed.");
});
