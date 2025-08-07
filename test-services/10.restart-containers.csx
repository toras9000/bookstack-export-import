#r "nuget: Lestaly.General, 0.102.0"
#load ".settings.csx"
#nullable enable
using Lestaly;
using Lestaly.Cx;

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-pause"), async () =>
{
    WriteLine("Restart service");
    await "docker".args("compose", "--file", settings.Instance1.Docker.Compose, "down", "--remove-orphans").echo();
    await "docker".args("compose", "--file", settings.Instance2.Docker.Compose, "down", "--remove-orphans").echo();
    await "docker".args("compose", "--file", settings.Instance1.Docker.Compose, "up", "-d", "--wait").echo().result().success();
    await "docker".args("compose", "--file", settings.Instance2.Docker.Compose, "up", "-d", "--wait").echo().result().success();
});
