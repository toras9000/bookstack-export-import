#r "nuget: Lestaly.General, 0.102.0"
#load ".settings.csx"
#nullable enable
using System.Threading;
using Lestaly;
using Lestaly.Cx;

return await Paved.ProceedAsync(async () =>
{
    WriteLine("Restart service");
    await "docker".args("compose", "--file", settings.Instance1.Docker.Compose, "down", "--remove-orphans").echo();
    await "docker".args("compose", "--file", settings.Instance2.Docker.Compose, "down", "--remove-orphans").echo();
    await "docker".args("compose", "--file", settings.Instance1.Docker.Compose, "up", "-d", "--wait").echo().result().success();
    await "docker".args("compose", "--file", settings.Instance2.Docker.Compose, "up", "-d", "--wait").echo().result().success();

    WriteLine();
    await "dotnet".args("script", ThisSource.RelativeFile("002-meke-api-token.csx"), "--", "--no-pause").result().success();

    WriteLine();
    await "dotnet".args("script", ThisSource.RelativeFile("010-show-url.csx"), "--", "--no-pause").result().success();
});
