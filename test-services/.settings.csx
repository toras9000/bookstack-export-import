#r "nuget: Lestaly.General, 0.102.0"
#nullable enable
using Lestaly;

var settings = new
{
    Instance1 = new InstanceSettings(
        Docker: new DockerSettings(
            Compose: ThisSource.RelativeFile("./docker/compose1.yml")
        ),
        Database: new DatabaseSettings(
            Port: 8810,
            Database: "bookstack_store",
            Username: "bookstack_user",
            Password: "bookstack_pass"
        ),
        BookStack: new BookStackSettings(
            Port: 8811,
            ApiTokenName: "TestToken",
            ApiTokenId: "00001111222233334444555566667777",
            ApiTokenSecret: "88889999aaaabbbbccccddddeeeeffff"
        )
    ),

    Instance2 = new InstanceSettings(
        Docker: new DockerSettings(
            Compose: ThisSource.RelativeFile("./docker/compose2.yml")
        ),
        Database: new DatabaseSettings(
            Port: 8820,
            Database: "bookstack_store",
            Username: "bookstack_user",
            Password: "bookstack_pass"
        ),
        BookStack: new BookStackSettings(
            Port: 8821,
            ApiTokenName: "TestToken",
            ApiTokenId: "444455556666777788889999aaaabbbb",
            ApiTokenSecret: "ccccddddeeeeffff0000111122223333"
        )
    ),
};

record DockerSettings(FileInfo Compose);
record DatabaseSettings(ushort Port, string Database, string Username, string Password);
record BookStackSettings(ushort Port, string ApiTokenName, string ApiTokenId, string ApiTokenSecret)
{
    public Uri Url { get; } = new Uri($"http://localhost:{Port}/");
    public Uri ApiEndpoint { get; } = new Uri($"http://localhost:{Port}/api/");
}
record InstanceSettings(DockerSettings Docker, DatabaseSettings Database, BookStackSettings BookStack);
