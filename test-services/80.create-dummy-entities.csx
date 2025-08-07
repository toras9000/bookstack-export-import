#r "nuget: BookStackApiClient, 25.7.0-lib.1"
#r "nuget: Kokuban, 0.2.0"
#r "nuget: Lestaly.General, 0.102.0"
#r "nuget: SkiaSharp, 3.119.0"
#r "nuget: Bogus, 35.6.3"
#load ".settings.csx"
#nullable enable
using System.Net.Http;
using System.Threading;
using System.Xml.Linq;
using Bogus;
using BookStackApiClient;
using BookStackApiClient.Utility;
using Kokuban;
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
    var random = new RandomContext(
        Books: new(Min: 3, Max: 6),
        Contents: new(Min: 3, Max: 6),
        SubPages: new(Min: 2, Max: 4),
        Images: new(Min: 2, Max: 3),
        Attaches: new(Min: 2, Max: 3),
        Shelves: new(Min: 3, Max: 3)
    );

    // Force option
    var forceGenerate = false;

    // Show info
    WriteLine($"Create dummy data in BookStack.");
    WriteLine($"BookStack Service URL : {settings.BookStack.Url}");

    // Create client and helper
    using var helper = new BookStackClientHelper(settings.BookStack.ApiEndpoint, settings.BookStack.ApiTokenId, settings.BookStack.ApiTokenSecret, cancelToken);
    helper.LimitHandler += (args) =>
    {
        WriteLine(Chalk.Yellow[$"Caught in API call rate limitation. Rate limit: {args.Exception.RequestsPerMin} [per minute], {args.Exception.RetryAfter} seconds to lift the limit."]);
        WriteLine(Chalk.Yellow[$"It will automatically retry after a period of time has elapsed."]);
        WriteLine(Chalk.Yellow[$"[Waiting...]"]);
        return ValueTask.CompletedTask;
    };

    // If not forced, check the status.
    if (!forceGenerate)
    {
        var books = await helper.Try((s, t) => s.ListBooksAsync(new(count: 1), t));
        if (0 < books.data.Length) throw new PavedMessageException("Some kind of book already exists.", PavedMessageKind.Warning);
    }

    // Generation context
    var context = new GenerationContext(settings.BookStack.Url, helper, random);

    // List to group books. Create shelves for later.
    var bookBinders = context.Random.Shelves.Range().Select(_ => new List<long>()).ToArray();

    // Create a dummy objects.
    for (var b = 0; b < context.Random.Books.Count(); b++)
    {
        var bookNum = 1 + b;
        WriteLine($"Create dummy Book {bookNum} ...");
        var bookCover = ContentGenerator.CreateTextImage($"Book {bookNum} Cover");
        var book = await helper.Try((c, t) => c.CreateBookAsync(new($"Book {bookNum}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), bookCover, $"cover.png", t));

        var binder = bookBinders.ElementAtOrDefault(Random.Shared.Next(bookBinders.Length + 1));    // Include outliers.
        binder?.Add(book.id);

        for (var c = 0; c < context.Random.Contents.Count(); c++)
        {
            var contentNum = 1 + c;
            if (context.Random.Fifty())
            {
                var pageLabel = $"B{bookNum}-P{contentNum}";
                var pageLink = $"B{bookNum}/P{contentNum}";
                WriteLine($"  Create dummy content {contentNum} Page ...");
                await createPageAsync(context, book.id, default, pageLabel, pageLink);
            }
            else
            {
                WriteLine($"  Create dummy content {contentNum} Chapter ...");
                var chapterLabel = $"B{bookNum}-C{contentNum}";
                var chapter = await helper.Try((c, t) => c.CreateChapterAsync(new(book.id, $"Chapter {chapterLabel}", $"Generated {DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}"), t));
                for (var p = 0; p < context.Random.SubPages.Count(); p++)
                {
                    var pageNum = 1 + p;
                    var pageLabel = $"B{bookNum}-C{contentNum}-P{pageNum}";
                    var pageLink = $"B{bookNum}/C{contentNum}/P{pageNum}";
                    WriteLine($"    Create dummy page {pageLabel} and materials ...");
                    await createPageAsync(context, book.id, chapter.id, pageLabel, pageLink);
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

public record RandomRange(int Min, int Max)
{
    public IEnumerable<int> Range() => Enumerable.Range(0, this.Count());
    public int Count() => Random.Shared.Next(this.Min, this.Max + 1);
}
public record RandomContext(RandomRange Books, RandomRange Contents, RandomRange SubPages, RandomRange Images, RandomRange Attaches, RandomRange Shelves)
{
    public bool Fifty() => Random.Shared.Next(2) == 0;
}
public record GenerationContext(Uri Url, BookStackClientHelper Helper, RandomContext Random);

async ValueTask<PageItem> createPageAsync(GenerationContext context, long book, long? chapter, string pageLabel, string linkPath)
{
    var doctype = context.Random.Fifty() ? "HTML" : "MD";
    var html = doctype == "HTML" ? ContentGenerator.CreatePageHtml(3) : default;
    var markdown = doctype == "MD" ? ContentGenerator.CreatePageMarkdown(3) : default;
    var page = await context.Helper.Try((c, t) => c.CreatePageAsync(new(name: $"Page {pageLabel} {doctype}", book, chapter, html, markdown), t));

    for (var i = 0; i < context.Random.Images.Count(); i++)
    {
        var imageLabel = $"{pageLabel}-{i}";
        var imageBin = ContentGenerator.CreateTextImage(imageLabel);
        var image = await context.Helper.Try((c, t) => c.CreateImageAsync(new(page.id, "gallery", $"Image-{imageLabel}"), imageBin, $"{imageLabel}.png", t));
        if (doctype == "HTML") html = html.TieIn($"""<br><a href="{image.url}" target="_blank" rel="noopener"><img src="{image.url}" alt="{imageLabel}"></a>""");
        else markdown = markdown.TieIn($"\n[![Image-{imageLabel}]({image.url})]({image.url})");
    }

    page = await context.Helper.Try((c, t) => c.UpdatePageAsync(page.id, new(html: html, markdown: markdown), t));

    for (var a = 0; a < context.Random.Attaches.Count(); a++)
    {
        var attachLabel = $"{pageLabel}-{a}";
        if (Random.Shared.Next(2) == 0)
        {
            var attachBin = Encoding.UTF8.GetBytes($"TextContent-{attachLabel}");
            var attach = await context.Helper.Try((c, t) => c.CreateFileAttachmentAsync(new($"Text-{attachLabel}", page.id), attachBin, $"{attachLabel}.txt", t));
        }
        else
        {
            var attachLink = new Uri(context.Url, $"/{linkPath}/{a}");
            var attach = await context.Helper.Try((c, t) => c.CreateLinkAttachmentAsync(new($"Text-{attachLabel}", page.id, attachLink.AbsoluteUri), t));
        }
    }

    return page;
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
