#r "nuget: BookStackApiClient, 23.12.1-lib.1"
#r "nuget: Kokuban, 0.2.0"
#nullable enable
using System.Threading;
using Kokuban;
using BookStackApiClient;
using System.Net.Http;

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
        this.cancelToken = cancelToken;
        this.msgWriter = output ?? Console.Out;
    }

    /// <summary>BookStack client instance</summary>
    public BookStackClient Client { get; }

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
                await Task.Delay(500 + (int)(ex.RetryAfter * 1000), this.cancelToken);
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

    public void Dispose()
    {
        this.Client.Dispose();
    }

    /// <summary>cancel token</summary>
    private readonly CancellationToken cancelToken;
    /// <summary>message output writer</summary>
    private readonly TextWriter msgWriter;
}
