#r "nuget: BookStackApiClient, 25.7.0-lib.1"
#r "nuget: Kokuban, 0.2.0"
#r "nuget: Lestaly.General, 0.102.0"
#load ".settings.csx"
#nullable enable
using System.Threading;
using BookStackApiClient;
using BookStackApiClient.Utility;
using Kokuban;
using Lestaly;

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-pause"), async () =>
{
    var instance = settings.Instance1;

    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = new SignalCancellationPeriod();

    // Show info
    WriteLine($"Delete all books in BookStack.");
    WriteLine($"BookStack Service URL : {instance.BookStack.Url}");

    // Create client and helper
    var apiEndpoint = instance.BookStack.ApiEndpoint;
    var apiToken = instance.BookStack.ApiTokenId;
    var apiSecret = instance.BookStack.ApiTokenSecret;
    using var helper = new BookStackClientHelper(apiEndpoint, apiToken, apiSecret, signal.Token);
    helper.LimitHandler += (args) =>
    {
        WriteLine(Chalk.Yellow[$"Caught in API call rate limitation. Rate limit: {args.Exception.RequestsPerMin} [per minute], {args.Exception.RetryAfter} seconds to lift the limit."]);
        WriteLine(Chalk.Yellow[$"It will automatically retry after a period of time has elapsed."]);
        WriteLine(Chalk.Yellow[$"[Waiting...]"]);
        return ValueTask.CompletedTask;
    };

    // Delete image gallery images
    WriteLine($"Delete Gallery Images");
    while (true)
    {
        // Get a list of images
        var images = await helper.Try((c, t) => c.ListImagesAsync(new(count: 100), t));
        if (images.data.Length <= 0) break;

        // Delete each image
        foreach (var image in images.data)
        {
            WriteLine($"..  Delete Image [{image.id}] {Chalk.Green[image.name]}");
            await helper.Try((c, t) => c.DeleteImageAsync(image.id, t));
        }
    }

    // Delete Books
    WriteLine($"Delete Books");
    while (true)
    {
        // Get a list of books
        var books = await helper.Try((c, t) => c.ListBooksAsync(new(count: 500), t));
        if (books.data.Length <= 0) break;

        // Delete each book
        foreach (var book in books.data)
        {
            WriteLine($"Delete [{book.id}] {Chalk.Green[book.name]}");
            await helper.Try((c, t) => c.DeleteBookAsync(book.id, t));
        }
    }

    // Delete Shelves
    WriteLine($"Delete Shelves");
    while (true)
    {
        // Get a list of shelves
        var shelves = await helper.Try((c, t) => c.ListShelvesAsync(new(count: 500), t));
        if (shelves.data.Length <= 0) break;

        // Delete each shelf
        foreach (var shelf in shelves.data)
        {
            WriteLine($"Delete [{shelf.id}] {Chalk.Green[shelf.name]}");
            await helper.Try((c, t) => c.DeleteShelfAsync(shelf.id, t));
        }
    }

    // Empty the trash
    WriteLine($"Destroy RecycleBin");
    while (true)
    {
        // Get a list of books
        var recycles = await helper.Try((c, t) => c.ListRecycleBinAsync(new(count: 500), t));
        if (recycles.data.Length <= 0) break;

        // Delete Trash Items
        foreach (var recycle in recycles.data)
        {
            WriteLine($"Destroy [{recycle.id}]");
            await helper.Try((c, t) => c.DestroyRecycleItemAsync(recycle.id, t));
        }
    }

    WriteLine($"Completed");
});

