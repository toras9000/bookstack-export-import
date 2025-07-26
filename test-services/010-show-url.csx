#r "nuget: Lestaly.General, 0.100.0"
#load ".settings.csx"
#nullable enable
using System.Threading;
using Lestaly;
using Lestaly.Cx;

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-pause"), async () =>
{
    await Task.CompletedTask;
    WriteLine($"Service URL");
    WriteLine($"  Instance1: {Poster.Link[settings.Instance1.BookStack.Url.AbsoluteUri]}");
    WriteLine($"  Instance2: {Poster.Link[settings.Instance2.BookStack.Url.AbsoluteUri]}");
});
