#r "nuget: Lestaly.General, 0.102.0"
#nullable enable
using Lestaly;
using Lestaly.Cx;

return await Paved.ProceedAsync(async () =>
{
    await "dotnet".args("script", ThisSource.RelativeFile("01.delete-data.csx"), "--no-pause").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("10.restart-containers.csx"), "--no-pause").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("11.meke-api-token.csx"), "--no-pause").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("80.create-dummy-entities.csx"), "--no-pause").echo().result().success();
    await "dotnet".args("script", ThisSource.RelativeFile("@show-service.csx"), "--no-pause").echo().result().success();
});
