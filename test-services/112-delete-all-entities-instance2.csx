#r "nuget: Lestaly, 0.83.0"
#load "../modules/.bookstack-api-helper.csx"
#load ".settings.csx"
#nullable enable
using System.Threading;
using BookStackApiClient;
using Kokuban;
using Lestaly;

await Paved.RunAsync(async () =>
{
    var instance = settings.Instance2;

    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = new SignalCancellationPeriod();

    // Show info
    WriteLine($"Delete all books in BookStack.");
    WriteLine($"BookStack Service URL : {instance.BookStack.Url}");

    // Create client and helper
    var apiKey = new ApiKey(instance.BookStack.ApiTokenId, instance.BookStack.ApiTokenSecret);
    using var helper = new BookStackClientHelper(instance.BookStack.ApiEndpoint, apiKey, cancelToken: signal.Token);

    // Delete image gallery images
    WriteLine($"Delete Gallery Images");
    while (true)
    {
        // Get a list of images
        var images = await helper.Try(s => s.ListImagesAsync(new(count: 500), cancelToken: signal.Token));
        if (images.data.Length <= 0) break;

        // Delete each image
        foreach (var image in images.data)
        {
            WriteLine($"..  Delete Image [{image.id}] {Chalk.Green[image.name]}");
            await helper.Try(s => s.DeleteImageAsync(image.id, cancelToken: signal.Token));
        }
    }

    // Delete Books
    WriteLine($"Delete Books");
    while (true)
    {
        // Get a list of books
        var books = await helper.Try(s => s.ListBooksAsync(new(count: 500), cancelToken: signal.Token));
        if (books.data.Length <= 0) break;

        // Delete each book
        foreach (var book in books.data)
        {
            WriteLine($"Delete [{book.id}] {Chalk.Green[book.name]}");
            await helper.Try(s => s.DeleteBookAsync(book.id, cancelToken: signal.Token));
        }
    }

    // Delete Shelves
    WriteLine($"Delete Shelves");
    while (true)
    {
        // Get a list of shelves
        var shelves = await helper.Try(s => s.ListShelvesAsync(new(count: 500), cancelToken: signal.Token));
        if (shelves.data.Length <= 0) break;

        // Delete each shelf
        foreach (var shelf in shelves.data)
        {
            WriteLine($"Delete [{shelf.id}] {Chalk.Green[shelf.name]}");
            await helper.Try(s => s.DeleteShelfAsync(shelf.id, cancelToken: signal.Token));
        }
    }

    // Empty the trash
    WriteLine($"Destroy RecycleBin");
    while (true)
    {
        // Get a list of books
        var recycles = await helper.Try(s => s.ListRecycleBinAsync(new(count: 500), cancelToken: signal.Token));
        if (recycles.data.Length <= 0) break;

        // Delete Trash Items
        foreach (var recycle in recycles.data)
        {
            WriteLine($"Destroy [{recycle.id}]");
            await helper.Try(s => s.DestroyRecycleItemAsync(recycle.id, cancelToken: signal.Token));
        }
    }

    WriteLine($"Completed");
});

