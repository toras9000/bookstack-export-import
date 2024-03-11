#r "nuget: BookStackApiClient, 24.2.0-lib.1"
#r "nuget: Kokuban, 0.2.0"
#nullable enable
using System.Threading;
using Kokuban;
using BookStackApiClient;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

/// <summary>API key information</summary>
/// <param name="Token">API token</param>
/// <param name="Secret">API secret</param>
public record ApiKey(string Token, string Secret);

/// <summary>
/// Auxiliary class for BookStackClient
/// </summary>
public class BookStackClientHelper : IDisposable
{
    /// <summary>Constructor that specifies the target to wrap.</summary>
    /// <param name="apiEndpoint">API endpoint.</param>
    /// <param name="apiKey">API Key.</param>
    /// <param name="output">The writer to which the message is output. null is stdout</param>
    /// <param name="cancelToken">Cancel tokens.</param>
    public BookStackClientHelper(Uri apiEndpoint, ApiKey apiKey, TextWriter? output = default, CancellationToken cancelToken = default)
    {
        this.Client = new BookStackClient(apiEndpoint, apiKey.Token, apiKey.Secret);
        this.Http = new HttpClient();
        this.CancelToken = cancelToken;
        this.msgWriter = output ?? Console.Out;
    }

    /// <summary>BookStack client instance</summary>
    public BookStackClient Client { get; }

    /// <summary>Http client instance</summary>
    public HttpClient Http { get; }

    /// <summary>cancel token</summary>
    public CancellationToken CancelToken { get; }

    /// <summary>Helper method to retry at API request limit</summary>
    /// <param name="accessor">API request processing</param>
    /// <typeparam name="TResult">API return type</typeparam>
    /// <returns>API return value</returns>
    public async ValueTask<TResult> Try<TResult>(Func<BookStackClient, Task<TResult>> accessor)
    {
        while (true)
        {
            try
            {
                return await accessor(this.Client).ConfigureAwait(true);
            }
            catch (ApiLimitResponseException ex)
            {
                this.msgWriter.WriteLine(Chalk.Yellow[$"Caught in API call rate limitation. Rate limit: {ex.RequestsPerMin} [per minute], {ex.RetryAfter} seconds to lift the limit."]);
                this.msgWriter.WriteLine(Chalk.Yellow[$"It will automatically retry after a period of time has elapsed."]);
                this.msgWriter.WriteLine(Chalk.Yellow[$"[Waiting...]"]);
                await Task.Delay(500 + (int)(ex.RetryAfter * 1000), this.CancelToken);
                this.msgWriter.WriteLine();
            }
        }
    }

    /// <summary>Helper method to retry at API request limit</summary>
    /// <param name="accessor">API request processing</param>
    public async ValueTask Try(Func<BookStackClient, Task> accessor)
    {
        await Try<int>(async c => { await accessor(c); return 0; });
    }

    /// <summary>Attempt to detect the version of BookStack.</summary>
    /// <param name="pageUri">BookStack page used for detection.</param>
    /// <returns>Task to obtain version.</returns>
    public async ValueTask<BookStackVersion?> DetectBookStackVersionAsync(Uri pageUri)
    {
        var page = await this.Http.GetStringAsync(pageUri);
        var detector = new Regex(@"""http.+\.css\?version=v(.+)""");
        var match = detector.Match(page);
        if (match.Success && BookStackVersion.TryParse(match.Groups[1].Value, out var version))
        {
            return version;
        }
        return default;
    }

    /// <summary>Attempts to acquire information about the API user itself.</summary>
    /// <returns>Task to obtain user information.</returns>
    public async ValueTask<User> GetMeAsync()
    {
        // It is not possible to retrieve information about oneself through the API.
        // I hope the following suggestions will pass.
        // https://github.com/BookStackApp/BookStack/issues/4321
        // This may be difficult since the API appears to be aimed at automating tasks rather than for external tools.

        // Here, as an alternative method, the search API is used to search for the user's own items.
        var found = await this.Try(c => c.SearchAsync(new("{owned_by:me}", count: 1), this.CancelToken));
        var item = found.data.FirstOrDefault();
        var owner = item switch
        {
            SearchContentBook => (await this.Try(c => c.ReadBookAsync(item.id, this.CancelToken))).owned_by,
            SearchContentChapter => (await this.Try(c => c.ReadChapterAsync(item.id, this.CancelToken))).owned_by,
            SearchContentPage => (await this.Try(c => c.ReadPageAsync(item.id, this.CancelToken))).owned_by,
            SearchContentShelf => (await this.Try(c => c.ReadShelfAsync(item.id, this.CancelToken))).owned_by,
            _ => new User(0, "Unknownw", "Unknown"),
        };

        return owner;
    }

    /// <summary>Dispose resources</summary>
    public void Dispose()
    {
        this.Client.Dispose();
        this.Http.Dispose();
    }

    /// <summary>message output writer</summary>
    private readonly TextWriter msgWriter;
}

/// <summary>
/// Data type representing the BookStack version
/// </summary>
public record BookStackVersion : IComparable<BookStackVersion>
{
    /// <summary>Constructor to decode version text.</summary>
    /// <param name="version">Version text</param>
    public BookStackVersion(string version)
    {
        var match = VersionPattern.Match(version);
        if (!match.Success) throw new ArgumentException("Illegal");
        var major = match.Groups["major"];
        var minor = match.Groups["minor"];
        var patch = match.Groups["patch"];
        this.Major = int.Parse(major.Value);
        this.Minor = int.Parse(minor.Value);
        this.Patch = patch.Success ? int.Parse(patch.Value) : 0;
        this.OriginalString = version;
    }

    /// <summary>Version text</summary>
    public string OriginalString { get; }

    /// <summary>Major version</summary>
    public int Major { get; }

    /// <summary>Minor version</summary>
    public int Minor { get; }

    /// <summary>Patch version</summary>
    public int Patch { get; }

    /// <summary>Try to parse version text</summary>
    /// <param name="text">Version text</param>
    /// <param name="version">Instance of parse result</param>
    /// <returns></returns>
    public static bool TryParse(string text, [NotNullWhen(true)] out BookStackVersion? version)
    {
        // Although the analysis should be performed with no exceptions, since this is not a performance-oriented area, it should be a simple process and not be transmitted to the outside world.
        try { version = new BookStackVersion(text); return true; }
        catch { version = default; return false; }
    }

    /// <inheritdoc />
    public int CompareTo(BookStackVersion? other)
    {
        if (other == null) return int.MaxValue;

        var major = NumberComparer.Compare(this.Major, other.Major);
        if (major != 0) return major;
        var minor = NumberComparer.Compare(this.Minor, other.Minor);
        if (minor != 0) return minor;
        return NumberComparer.Compare(this.Patch, other.Patch);
    }

    /// <summary>An operator that compares the size of an instance.</summary>
    public static bool operator <(BookStackVersion x, BookStackVersion y) => x.CompareTo(y) < 0;
    /// <summary>An operator that compares the size of an instance.</summary>
    public static bool operator >(BookStackVersion x, BookStackVersion y) => x.CompareTo(y) > 0;
    /// <summary>An operator that compares the size of an instance.</summary>
    public static bool operator <=(BookStackVersion x, BookStackVersion y) => x.CompareTo(y) <= 0;
    /// <summary>An operator that compares the size of an instance.</summary>
    public static bool operator >=(BookStackVersion x, BookStackVersion y) => x.CompareTo(y) >= 0;

    private static readonly Regex VersionPattern = new(@"^\s*(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?\s*$");
    private static readonly Comparer<int> NumberComparer = Comparer<int>.Default;

}


public static async IAsyncEnumerable<BookSummary> EnumerateAllBooksAsync(this BookStackClientHelper self)
{
    var offset = 0;
    while (true)
    {
        var books = await self.Try(c => c.ListBooksAsync(new(offset, count: 500), self.CancelToken));
        foreach (var book in books.data)
        {
            yield return book;
        }

        offset += books.data.Length;
        var finished = (books.data.Length <= 0) || (books.total <= offset);
        if (finished) break;
    }
}

public static async IAsyncEnumerable<UserSummary> EnumerateAllUsersAsync(this BookStackClientHelper self)
{
    var offset = 0;
    var allUsers = new List<UserSummary>();
    while (true)
    {
        var users = await self.Try(c => c.ListUsersAsync(new(offset, count: 500), self.CancelToken));
        foreach (var user in users.data)
        {
            yield return user;
        }

        offset += users.data.Length;
        var finished = (users.data.Length <= 0) || (users.total <= offset);
        if (finished) break;
    }
}

public static async IAsyncEnumerable<RoleSummary> EnumerateAllRolesAsync(this BookStackClientHelper self)
{
    var offset = 0;
    var allRoles = new List<RoleSummary>();
    while (true)
    {
        var roles = await self.Try(c => c.ListRolesAsync(new(offset, count: 500), self.CancelToken));
        foreach (var role in roles.data)
        {
            yield return role;
        }

        offset += roles.data.Length;
        var finished = (roles.data.Length <= 0) || (roles.total <= offset);
        if (finished) break;
    }
}
