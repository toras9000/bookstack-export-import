#r "nuget: Lestaly.General, 0.100.0"
#r "nuget: SkiaSharp, 3.119.0"
#r "nuget: Bogus, 35.6.3"
#load "../modules/.bookstack-helper.csx"
#load ".settings.csx"
#nullable enable
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using Bogus;
using BookStackApiClient;
using BookStackApiClient.Utility;
using Lestaly;
using SkiaSharp;

return await Paved.ProceedAsync(noPause: Args.RoughContains("--no-pause"), async () =>
{
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = new SignalCancellationPeriod();

    WriteLine($"Instance1");
    await createDummyBooksAsync(settings.Instance1, signal.Token);
});

async Task createDummyBooksAsync(InstanceSettings settings, CancellationToken cancelToken)
{
    // Number of objects to be generated.
    var genBooks = new { min = 3, max = 6, };
    var genContents = new { min = 3, max = 6, };
    var genSubPages = new { min = 2, max = 4, };
    var genPageImages = new { min = 2, max = 3, };
    var genPageAttaches = new { min = 2, max = 3, };
    var genShelves = 3;

    // Force option
    var forceGenerate = false;

    // Show info
    WriteLine($"Create dummy data in BookStack.");
    WriteLine($"BookStack Service URL : {settings.BookStack.Url}");

    // Create client and helper
    var apiEndpoint = settings.BookStack.ApiEndpoint;
    var apiToken = settings.BookStack.ApiTokenId;
    var apiSecret = settings.BookStack.ApiTokenSecret;
    using var helper = new BookStackClientHelper(apiEndpoint, apiToken, apiSecret, cancelToken);
    helper.HandleLimitMessage();

    // If not forced, check the status.
    if (!forceGenerate)
    {
        var books = await helper.Try((s, t) => s.ListBooksAsync(new(count: 1), t));
        if (0 < books.data.Length) throw new PavedMessageException("Some kind of book already exists.", PavedMessageKind.Warning);
    }

    // List to group books. Create shelves for later.
    var bookBinders = Enumerable.Range(0, genShelves).Select(_ => new List<long>()).ToArray();

    // Create a dummy objects.
    var bookCount = Random.Shared.Next(genBooks.min, genBooks.max + 1);
    for (var b = 0; b < bookCount; b++)
    {
        var bookNum = 1 + b;
        WriteLine($"Create dummy Book {bookNum} ...");
        var bookCover = ContentGenerator.CreateTextImage($"Book {bookNum} Cover");
        var book = await helper.Try((c, t) => c.CreateBookAsync(new($"Book {bookNum}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), bookCover, $"cover.png", t));

        var binder = bookBinders.ElementAtOrDefault(Random.Shared.Next(bookBinders.Length + 1));    // Include outliers.
        binder?.Add(book.id);

        var contentCount = Random.Shared.Next(genContents.min, genContents.max + 1);
        for (var c = 0; c < contentCount; c++)
        {
            var contentNum = 1 + c;
            if (Random.Shared.Next(2) == 0)
            {
                var pageLabel = $"B{bookNum}-P{contentNum}";
                WriteLine($"  Create dummy content {contentNum} Page ...");
                var page = Random.Shared.Next(2) switch
                {
                    0 => await helper.Try((c, t) => c.CreateMarkdownPageInBookAsync(new(book.id, $"Page {pageLabel}", ContentGenerator.CreatePageMarkdown(3)), t)),
                    _ => await helper.Try((c, t) => c.CreateHtmlPageInBookAsync(new(book.id, $"Page {pageLabel}", ContentGenerator.CreatePageHtml(3)), t)),
                };

                await createPageMaterials(page, pageLabel, $"B{bookNum}/P{contentNum}");
            }
            else
            {
                WriteLine($"  Create dummy content {contentNum} Chapter ...");
                var chapterLabel = $"B{bookNum}-C{contentNum}";
                var chapter = await helper.Try((c, t) => c.CreateChapterAsync(new(book.id, $"Chapter {chapterLabel}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), t));
                var pageCount = Random.Shared.Next(genSubPages.min, genSubPages.max + 1);
                for (var p = 0; p < pageCount; p++)
                {
                    var pageNum = 1 + p;
                    var pageLabel = $"B{bookNum}-C{contentNum}-P{pageNum}";
                    WriteLine($"    Create dummy page {pageLabel} and materials ...");
                    var page = Random.Shared.Next(2) switch
                    {
                        0 => await helper.Try((c, t) => c.CreateMarkdownPageInChapterAsync(new(chapter.id, $"Page {pageLabel}", ContentGenerator.CreatePageMarkdown(3)), t)),
                        _ => await helper.Try((c, t) => c.CreateHtmlPageInChapterAsync(new(chapter.id, $"Page {pageLabel}", ContentGenerator.CreatePageHtml(3)), t)),
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
                    var image = await helper.Try((c, t) => c.CreateImageAsync(new(page.id, "gallery", $"Image-{imageLabel}"), imageBin, $"{imageLabel}.png", t));
                }

                var attachCount = Random.Shared.Next(genPageAttaches.min, genPageAttaches.max + 1);
                for (var a = 0; a < attachCount; a++)
                {
                    var attachLabel = $"{pageLabel}-{a}";
                    if (Random.Shared.Next(2) == 0)
                    {
                        var attachBin = Encoding.UTF8.GetBytes($"TextContent-{attachLabel}");
                        var attach = await helper.Try((c, t) => c.CreateFileAttachmentAsync(new($"Text-{attachLabel}", page.id), attachBin, $"{attachLabel}.txt", t));
                    }
                    else
                    {
                        var attachLink = new Uri(settings.BookStack.Url, $"/{linkPath}/{a}");
                        var attach = await helper.Try((c, t) => c.CreateLinkAttachmentAsync(new($"Text-{attachLabel}", page.id, attachLink.AbsoluteUri), t));
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
        await helper.Try((c, t) => c.CreateShelfAsync(new($"Shelf {binder.num}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}", books: binder.books), imageBin, $"{imageLabel}.png", t));
    }

    WriteLine($"Completed");
}

/// <summary>
/// Content data generator
/// </summary>
public static class ContentGenerator
{
    public static string CreatePageMarkdown(int paragraphs)
        => $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}\n\n{new Faker("en").Lorem.Paragraphs(paragraphs, "\n\n")}";

    public static string CreatePageHtml(int paragraphs)
        => $"<p><span>Generated <time>{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}</time></span></p><p>{new Faker("en").Lorem.Paragraphs(paragraphs, "</p><p>")}</p>";

    public static byte[] CreateRectImage(float x, float y, float width, float height, int imgWidth = 200, int imgHeight = 150, uint fgcolor = 0xFF0000FF, uint bgcolor = 0xFFFFFFFF, string format = "png")
        => CreateImage((canvas, font, painter) => canvas.DrawRect(x, y, width, height, painter), imgWidth, imgHeight, fgcolor, bgcolor, format);

    public static byte[] CreateTextImage(string text, int imgWidth = 200, int imgHeight = 150, uint fgcolor = 0xFF000000, uint bgcolor = 0xFF808080, string format = "png")
        => CreateImage((canvas, font, painter) => canvas.DrawText(text, 5f, font.Size / 2 + imgHeight / 2, font, painter), imgWidth, imgHeight, fgcolor, bgcolor, format);

    public static byte[] CreateCircleImage(float x, float y, float radius, int imgWidth = 200, int imgHeight = 150, uint fgcolor = 0xFF0000FF, uint bgcolor = 0xFFFFFFFF, string format = "png")
        => CreateImage((canvas, font, painter) => canvas.DrawCircle(x, y, radius, painter), imgWidth, imgHeight, fgcolor, bgcolor, format);

    public static byte[] CreateImage(Action<SKCanvas, SKFont, SKPaint> drawer, int width, int height, uint fgcolor, uint bgcolor, string format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888));
        var font = new SKFont();
        var paint = new SKPaint()
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(fgcolor),
        };
        surface.Canvas.Clear(new SKColor(bgcolor));
        drawer(surface.Canvas, font, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(Enum.Parse<SKEncodedImageFormat>(format, ignoreCase: true), 100);

        return data.ToArray();
    }

}
