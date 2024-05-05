#r "nuget: Lestaly, 0.62.0"
#r "nuget: SkiaSharp, 2.88.8"
#load "../modules/.bookstack-api-helper.csx"
#nullable enable
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using BookStackApiClient;
using Lestaly;
using SkiaSharp;

await Paved.RunAsync(async () =>
{
    // BookStack service URL.
    var serviceUri = new Uri("http://localhost:9971/");

    // API Token and Secret Key
    var apiToken = "00001111222233334444555566667777";
    var apiSecret = "88889999aaaabbbbccccddddeeeeffff";

    // Number of objects to be generated.
    var genBooks = new { min = 3, max = 6, };
    var genContents = new { min = 3, max = 6, };
    var genSubPages = new { min = 2, max = 4, };
    var genPageImages = new { min = 2, max = 3, };
    var genPageAttaches = new { min = 2, max = 3, };
    var genShelves = 3;

    // Force option
    var forceGenerate = false;

    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = ConsoleWig.CreateCancelKeyHandlePeriod();

    // Show info
    Console.WriteLine($"Create dummy data in BookStack.");
    Console.WriteLine($"BookStack Service URL : {serviceUri}");

    // Create client and helper
    var apiUri = new Uri(serviceUri, "/api/");
    var apiKey = new ApiKey(apiToken, apiSecret);
    using var helper = new BookStackClientHelper(apiUri, apiKey, cancelToken: signal.Token);

    // If not forced, check the status.
    if (!forceGenerate)
    {
        var books = await helper.Try(s => s.ListBooksAsync(cancelToken: signal.Token));
        if (0 < books.total)
        {
            throw new PavedMessageException($"Some kind of book already exists.", PavedMessageKind.Warning);
        }
    }

    // List to group books. Create shelves for later.
    var bookBinders = Enumerable.Range(0, genShelves).Select(_ => new List<long>()).ToArray();

    // Create a dummy objects.
    var bookCount = Random.Shared.Next(genBooks.min, genBooks.max + 1);
    for (var b = 0; b < bookCount; b++)
    {
        var bookNum = 1 + b;
        Console.WriteLine($"Create dummy Book {bookNum} ...");
        var bookCover = ContentGenerator.CreateTextImage($"Book {bookNum} Cover");
        var book = await helper.Try(s => s.CreateBookAsync(new($"Book {bookNum}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), bookCover, $"cover.png", cancelToken: signal.Token));

        var binder = bookBinders.ElementAtOrDefault(Random.Shared.Next(bookBinders.Length + 1));    // Include outliers.
        binder?.Add(book.id);

        var contentCount = Random.Shared.Next(genContents.min, genContents.max + 1);
        for (var c = 0; c < contentCount; c++)
        {
            var contentNum = 1 + c;
            if (Random.Shared.Next(2) == 0)
            {
                var pageLabel = $"B{bookNum}-P{contentNum}";
                Console.WriteLine($"  Create dummy content {contentNum} Page ...");
                var page = Random.Shared.Next(2) switch
                {
                    0 => await helper.Try(s => s.CreateMarkdownPageInBookAsync(new(book.id, $"Page {pageLabel}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), signal.Token)),
                    _ => await helper.Try(s => s.CreateHtmlPageInBookAsync(new(book.id, $"Page {pageLabel}", $"<span>Generated <time>{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}</time></span>"), signal.Token)),
                };

                await createPageMaterials(page, pageLabel, $"B{bookNum}/P{contentNum}");
            }
            else
            {
                Console.WriteLine($"  Create dummy content {contentNum} Chapter ...");
                var chapterLabel = $"B{bookNum}-C{contentNum}";
                var chapter = await helper.Try(s => s.CreateChapterAsync(new(book.id, $"Chapter {chapterLabel}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), signal.Token));
                var pageCount = Random.Shared.Next(genSubPages.min, genSubPages.max + 1);
                for (var p = 0; p < pageCount; p++)
                {
                    var pageNum = 1 + p;
                    var pageLabel = $"B{bookNum}-C{contentNum}-P{pageNum}";
                    Console.WriteLine($"    Create dummy page {pageLabel} and materials ...");
                    var page = Random.Shared.Next(2) switch
                    {
                        0 => await helper.Try(s => s.CreateMarkdownPageInChapterAsync(new(chapter.id, $"Page {pageLabel}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), signal.Token)),
                        _ => await helper.Try(s => s.CreateHtmlPageInChapterAsync(new(chapter.id, $"Page {pageLabel}", $"<span>Generated <time>{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}</time></span>"), signal.Token)),
                    };

                    await createPageMaterials(page, pageLabel, $"B{bookNum}/C{contentNum}/P{pageNum}");
                }
            }

            async ValueTask createPageMaterials(PageItem page, string pageLabel, string linkPath)
            {
                var imageCount = Random.Shared.Next(genPageImages.min, genPageImages.max + 1);
                for (var i = 0; i < imageCount; i++)
                {
                    var imageLabel = $"{pageLabel}-{i}";
                    var imageBin = ContentGenerator.CreateTextImage(imageLabel);
                    var image = await helper.Try(s => s.CreateImageAsync(new(page.id, "gallery", $"Image-{imageLabel}"), imageBin, $"{imageLabel}.png", signal.Token));
                }

                var attachCount = Random.Shared.Next(genPageAttaches.min, genPageAttaches.max + 1);
                for (var a = 0; a < attachCount; a++)
                {
                    var attachLabel = $"{pageLabel}-{a}";
                    if (Random.Shared.Next(2) == 0)
                    {
                        var attachBin = Encoding.UTF8.GetBytes($"TextContent-{attachLabel}");
                        var attach = await helper.Try(s => s.CreateFileAttachmentAsync(new($"Text-{attachLabel}", page.id), attachBin, $"{attachLabel}.txt", signal.Token));
                    }
                    else
                    {
                        var attachLink = $"http://localhost/{linkPath}/{a}";
                        var attach = await helper.Try(s => s.CreateLinkAttachmentAsync(new($"Text-{attachLabel}", page.id, attachLink), signal.Token));
                    }
                }
            }

        }
    }

    // Create shelves
    foreach (var binder in bookBinders.Select((books, idx) => (books, num: 1 + idx)))
    {
        var imageLabel = $"Shelf-{binder.num}";
        var imageBin = ContentGenerator.CreateTextImage(imageLabel);
        await helper.Try(s => s.CreateShelfAsync(new($"Shelf {binder.num}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}", books: binder.books), imageBin, $"{imageLabel}.png", cancelToken: signal.Token));
    }

    Console.WriteLine($"Completed");
});


/// <summary>
/// Content data generator
/// </summary>
public static class ContentGenerator
{
    public static byte[] CreateRectImage(float x, float y, float width, float height, int imgWidth = 200, int imgHeight = 150, uint fgcolor = 0xFF0000FF, uint bgcolor = 0xFFFFFFFF, string format = "png")
        => CreateImage((canvas, painter) => canvas.DrawRect(x, y, width, height, painter), imgWidth, imgHeight, fgcolor, bgcolor, format);

    public static byte[] CreateTextImage(string text, int imgWidth = 200, int imgHeight = 150, uint fgcolor = 0xFF000000, uint bgcolor = 0xFF808080, string format = "png")
        => CreateImage((canvas, painter) => canvas.DrawText(text, 5f, painter.TextSize / 2 + imgHeight / 2, painter), imgWidth, imgHeight, fgcolor, bgcolor, format);

    public static byte[] CreateCircleImage(float x, float y, float radius, int imgWidth = 200, int imgHeight = 150, uint fgcolor = 0xFF0000FF, uint bgcolor = 0xFFFFFFFF, string format = "png")
        => CreateImage((canvas, painter) => canvas.DrawCircle(x, y, radius, painter), imgWidth, imgHeight, fgcolor, bgcolor, format);

    public static byte[] CreateImage(Action<SKCanvas, SKPaint> drawer, int width, int height, uint fgcolor, uint bgcolor, string format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888));
        var paint = new SKPaint()
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(fgcolor),
        };
        surface.Canvas.Clear(new SKColor(bgcolor));
        drawer(surface.Canvas, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(Enum.Parse<SKEncodedImageFormat>(format, ignoreCase: true), 100);

        return data.ToArray();
    }

}
