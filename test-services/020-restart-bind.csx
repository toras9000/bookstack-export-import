#r "nuget: Lestaly, 0.67.0"
#load ".compose-helper.csx"
#nullable enable
using System.Net.Http;
using System.Threading;
using Lestaly;
using Lestaly.Cx;

await Paved.RunAsync(config: c => c.AnyPause(), action: async () =>
{
    WriteLine("Restart service (with bind-mount)");
    await composeRestartAsync(volumeBind: true);

    WriteLine();
    WriteLine("Container up completed.");
    WriteLine("Service URL");
    WriteLine($" {Poster.Link[$"http://localhost:{await composeGetPublishPort(1)}"]}");
    WriteLine($" {Poster.Link[$"http://localhost:{await composeGetPublishPort(2)}"]}");
    WriteLine();
});
