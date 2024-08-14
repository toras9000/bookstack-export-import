#r "nuget: Lestaly, 0.67.0"
#load ".compose-helper.csx"
#nullable enable
using System.Threading;
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(async () =>
{
    Console.WriteLine("Stop service");
    await composeDownAsync().silent();

    Console.WriteLine("Delete volumes");
    ThisSource.RelativeDirectory("./docker/volumes").DeleteRecurse();

    Console.WriteLine("completed.");
});
