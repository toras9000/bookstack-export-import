#r "nuget: Lestaly, 0.83.0"
#r "nuget: Dapper, 2.1.66"
#r "nuget: MySqlConnector, 2.4.0"
#r "nuget: BCrypt.Net-Next, 4.0.3"
#load ".settings.csx"
#nullable enable
using Dapper;
using Lestaly;
using MySqlConnector;

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-pause"), async () =>
{
    WriteLine($"Instance1");
    await makeTestApiTokenAsync(settings.Instance1);
    WriteLine($"Instance2");
    await makeTestApiTokenAsync(settings.Instance2);
});

async Task makeTestApiTokenAsync(InstanceSettings settings)
{
    WriteLine("Setup api token ...");
    var config = new MySqlConnectionStringBuilder();
    config.Server = "localhost";
    config.Port = settings.Database.Port;
    config.UserID = settings.Database.Username;
    config.Password = settings.Database.Password;
    config.Database = settings.Database.Database;

    using var mysql = new MySqlConnection(config.ConnectionString);
    await mysql.OpenAsync();

    var tokenExists = await mysql.QueryFirstAsync<long>("select count(*) from api_tokens where name = @name", param: new { name = settings.BookStack.ApiTokenName, });
    if (0 < tokenExists)
    {
        WriteLine(".. Already exists");
        return;
    }

    var adminId = await mysql.QueryFirstAsync<long>(sql: "select id from users where name = 'Admin'");
    var hashSalt = BCrypt.Net.BCrypt.GenerateSalt(12, 'y');
    var secretHash = BCrypt.Net.BCrypt.HashPassword(settings.BookStack.ApiTokenSecret, hashSalt);
    var tokenParam = new
    {
        name = settings.BookStack.ApiTokenName,
        token_id = settings.BookStack.ApiTokenId,
        secret = secretHash,
        user_id = adminId,
        expires_at = DateTime.Now.AddYears(100),
    };
    await mysql.ExecuteAsync(
        sql: "insert into api_tokens (name, token_id, secret, user_id, expires_at) values (@name, @token_id, @secret, @user_id, @expires_at)",
        param: tokenParam
    );
    WriteLine(".. Token added");
}
