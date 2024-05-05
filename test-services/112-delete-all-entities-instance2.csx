#r "nuget: Lestaly, 0.62.0"
#load "../modules/.bookstack-api-helper.csx"
#nullable enable
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using BookStackApiClient;
using Kokuban;
using Lestaly;

await Paved.RunAsync(async () =>
{
    // BookStack service URL.
    var serviceUri = new Uri("http://localhost:9972/");

    // API Token and Secret Key
    var apiToken = "444455556666777788889999aaaabbbb";
    var apiSecret = "ccccddddeeeeffff0000111122223333";

    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = ConsoleWig.CreateCancelKeyHandlePeriod();

    // Show info
    Console.WriteLine($"Delete all books in BookStack.");
    Console.WriteLine($"BookStack Service URL : {serviceUri}");

    // Create client and helper
    var apiUri = new Uri(serviceUri, "/api/");
    var apiKey = new ApiKey(apiToken, apiSecret);
    using var helper = new BookStackClientHelper(apiUri, apiKey, cancelToken: signal.Token);

    // Delete image gallery images
    Console.WriteLine($"Delete Gallery Images");
    while (true)
    {
        // Get a list of images
        var images = await helper.Try(s => s.ListImagesAsync(new(count: 500), cancelToken: signal.Token));
        if (images.data.Length <= 0) break;

        // Delete each image
        foreach (var image in images.data)
        {
            Console.WriteLine($"..  Delete Image [{image.id}] {Chalk.Green[image.name]}");
            await helper.Try(s => s.DeleteImageAsync(image.id, cancelToken: signal.Token));
        }
    }

    // Delete Book
    Console.WriteLine($"Delete Books");
    while (true)
    {
        // Get a list of books
        var books = await helper.Try(s => s.ListBooksAsync(new(count: 500), cancelToken: signal.Token));
        if (books.data.Length <= 0) break;

        // Delete each book
        foreach (var book in books.data)
        {
            Console.WriteLine($"Delete [{book.id}] {Chalk.Green[book.name]}");
            await helper.Try(s => s.DeleteBookAsync(book.id, cancelToken: signal.Token));
        }
    }

    // Empty the trash
    Console.WriteLine($"Destroy RecycleBin");
    while (true)
    {
        // Get a list of books
        var recycles = await helper.Try(s => s.ListRecycleBinAsync(new(count: 500), cancelToken: signal.Token));
        if (recycles.data.Length <= 0) break;

        // Delete Trash Items
        foreach (var recycle in recycles.data)
        {
            Console.WriteLine($"Destroy [{recycle.id}]");
            await helper.Try(s => s.DestroyRecycleItemAsync(recycle.id, cancelToken: signal.Token));
        }
    }

    Console.WriteLine($"Completed");
});

