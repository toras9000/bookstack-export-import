#r "nuget: Lestaly.General, 0.102.0"
#load ".settings.csx"
#nullable enable
using Lestaly;
using Lestaly.Cx;

return await Paved.ProceedAsync(async () =>
{
    WriteLine("Stop service & remove volume");
    await "docker".args("compose", "--file", settings.Instance1.Docker.Compose, "down", "--remove-orphans", "--volumes").echo().result().success();
    await "docker".args("compose", "--file", settings.Instance2.Docker.Compose, "down", "--remove-orphans", "--volumes").echo().result().success();
});
