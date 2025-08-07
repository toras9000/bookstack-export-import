#r "nuget: BookStackApiClient, 25.7.0-lib.1"
#nullable enable
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using BookStackApiClient;
using BookStackApiClient.Utility;

/// <summary>API key information</summary>
/// <param name="Token">API token</param>
/// <param name="Secret">API secret</param>
public record ApiKey(string Token, string Secret);

/// <summary>Attempt to detect the version of BookStack.</summary>
/// <param name="self">BookStackClientHelper instance</param>
/// <param name="pageUri">BookStack page used for detection.</param>
/// <param name="breaker">Cancellation token.</param>
/// <returns>Task to obtain version.</returns>
public static async ValueTask<BookStackVersion?> DetectBookStackVersionAsync(this HttpClient self, Uri pageUri, CancellationToken breaker = default)
{
    var page = await self.GetStringAsync(pageUri, breaker);
    var detector = new Regex(@"""http.+\.css\?version=v(.+)""");
    var match = detector.Match(page);
    if (match.Success && BookStackVersion.TryParse(match.Groups[1].Value, out var version))
    {
        return version;
    }
    return default;
}

/// <summary>Attempts to acquire information about the API user itself.</summary>
/// <param name="self">BookStackClientHelper instance</param>
/// <returns>Task to obtain user information.</returns>
public static async ValueTask<User> GetMeAsync(this BookStackClientHelper self)
{
    // It is not possible to retrieve information about oneself through the API.
    // I hope the following suggestions will pass.
    // https://github.com/BookStackApp/BookStack/issues/4321
    // This may be difficult since the API appears to be aimed at automating tasks rather than for external tools.

    // Here, as an alternative method, the search API is used to search for the user's own items.
    var found = await self.Try((c, t) => c.SearchAsync(new("{owned_by:me}", count: 1), t));
    var item = found.data.FirstOrDefault();
    var owner = item switch
    {
        SearchContentBook => (await self.Try((c, t) => c.ReadBookAsync(item.id, t))).owned_by,
        SearchContentChapter => (await self.Try((c, t) => c.ReadChapterAsync(item.id, t))).owned_by,
        SearchContentPage => (await self.Try((c, t) => c.ReadPageAsync(item.id, t))).owned_by,
        SearchContentShelf => (await self.Try((c, t) => c.ReadShelfAsync(item.id, t))).owned_by,
        _ => new User(0, "Unknownw", "Unknown"),
    };

    return owner;
}
