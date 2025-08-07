#r "nuget: Lestaly.General, 0.102.0"
#load "modules/.bookstack-helper.csx"
#load "modules/.bookstack-data.csx"
#nullable enable
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BookStackApiClient;
using BookStackApiClient.Utility;
using Kokuban;
using Lestaly;

var settings = new
{
    BookStack = new
    {
        // Target BookStack service address
        ServiceUrl = new Uri("http://localhost:8811/"),

        // API token of the user performing the export
        ApiToken = "00001111222233334444555566667777",

        // API secret of the user performing the export
        ApiSecret = "88889999aaaabbbbccccddddeeeeffff",
    },

    Local = new
    {
        // Destination directory for export data.
        ExportDir = ThisSource.RelativeDirectory("exports"),
    },
};

return await Paved.ProceedAsync(async () =>
{
    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = new SignalCancellationPeriod();

    // Title display
    WriteLine($"Exporting data from BookStack");
    WriteLine($"  Service Address: {settings.BookStack.ServiceUrl}");
    WriteLine();

    // Create client and helper
    var apiUri = new Uri(settings.BookStack.ServiceUrl, "/api/");
    var apiKey = new ApiKey(settings.BookStack.ApiToken, settings.BookStack.ApiSecret);
    using var http = new HttpClient();
    using var client = new BookStackClient(apiUri, apiKey.Token, apiKey.Secret);
    using var helper = new BookStackClientHelper(client, signal.Token);
    helper.HandleLimitMessage();

    // Detect BookStack version
    var version = await http.DetectBookStackVersionAsync(settings.BookStack.ServiceUrl);
    if (version == null) throw new PavedMessageException("Unable to detect BookStack version.", PavedMessageKind.Warning);
    if (version < BookStackVersion.Parse("23.12.0")) throw new PavedMessageException("Unsupported BookStack version.", PavedMessageKind.Warning);

    // Obtain information about myself. This may not be obtained.
    var user_me = await helper.GetMeAsync();

    // Determine output directory
    var exportTime = DateTime.Now;
    var exportDir = settings.Local.ExportDir.RelativeDirectory($"{exportTime:yyyy.MM.dd-HH.mm.ss}").WithCreate();
    WriteLine($"Export to {exportDir.FullName}");
    WriteLine();

    // Options for saving JSON
    var jsonOptions = new JsonSerializerOptions();
    jsonOptions.WriteIndented = true;

    // Create context instance
    var context = new ExportContext(helper, http, jsonOptions, exportDir, signal.Token);

    // Output export information
    var exportMeta = new ExportMetadata(settings.BookStack.ServiceUrl.AbsoluteUri, version, exportTime, user_me);
    await exportDir.RelativeFile("export-meta.json").WriteJsonAsync(exportMeta, jsonOptions);

    // Retrieve information for each book
    await foreach (var summary in context.Helper.EnumerateAllBooksAsync())
    {
        // Indicate the status.
        WriteLine($"Exporting book: {Chalk.Green[summary.name]} ...");

        // Read book information
        var book = await context.Helper.Try((c, _) => c.ReadBookAsync(summary.id, context.CancelToken));
        var bookPerms = await context.Helper.Try((c, _) => c.ReadBookPermissionsAsync(summary.id, context.CancelToken));

        // Save book information
        var bookDir = exportDir.RelativeDirectory($"{book.id:D4}B.{book.name.ToFileName()}").WithCreate();
        var bookMeta = createMetadata(book, bookPerms);
        await bookDir.RelativeFile("book-meta.json").WriteJsonAsync(bookMeta, context.JsonOptions);

        // Download and save the book cover, if available.
        if (book.cover != null && book.cover.url.IsNotWhite())
        {
            var coverUrl = new Uri(book.cover.url);
            var coverFile = bookDir.RelativeFile($"book-cover{Path.GetExtension(coverUrl.AbsolutePath)}");
            await context.Http.GetFileAsync(coverUrl, coverFile.FullName, context.CancelToken);
        }

        // Save Book Contents
        foreach (var content in book.contents.OrderBy(c => c.priority))
        {
            // Save process according to content
            if (content is BookContentChapter chapterContent)
            {
                // Read chapter information
                WriteLine($"  Chapter: {Chalk.Green[chapterContent.name]} ...");
                var chapter = await context.Helper.Try((c, _) => c.ReadChapterAsync(chapterContent.id, context.CancelToken));
                var chapterPerms = await context.Helper.Try((c, _) => c.ReadChapterPermissionsAsync(chapterContent.id, context.CancelToken));

                // Save chapter information
                var chapterDir = bookDir.RelativeDirectory($"{chapter.priority:D4}C.{chapter.name.ToFileName()}").WithCreate();
                var chapterMeta = createMetadata(chapter, chapterPerms);
                await chapterDir.RelativeFile("chapter-meta.json").WriteJsonAsync(chapterMeta, context.JsonOptions);

                // Save each page in a chapter
                foreach (var pageContent in chapter.pages)
                {
                    WriteLine($"    Page: {Chalk.Green[pageContent.name]} ...");
                    await exportPageAsync(context, chapterDir, pageContent.id);
                }
            }
            else if (content is BookContentPage pageContent)
            {
                // Save page
                WriteLine($"  Page: {Chalk.Green[pageContent.name]} ...");
                await exportPageAsync(context, bookDir, pageContent.id);
            }
        }
    }

    // Shelf information storing directory
    var shelvesDir = exportDir.RelativeDirectory("shelves");

    // Retrieve information for each shelf
    await foreach (var summary in context.Helper.EnumerateAllShelvesAsync())
    {
        // Indicate the status.
        WriteLine($"Exporting shelf: {Chalk.Green[summary.name]} ...");

        // Read shelf information
        var shelf = await context.Helper.Try((c, _) => c.ReadShelfAsync(summary.id, context.CancelToken));
        var shelfPerms = await context.Helper.Try((c, _) => c.ReadShelfPermissionsAsync(summary.id, context.CancelToken));

        // Save shelf information
        var shelfMeta = createMetadata(shelf, shelfPerms);
        await shelvesDir.RelativeFile($"{shelf.id:D4}S-meta.json").WithDirectoryCreate().WriteJsonAsync(shelfMeta, context.JsonOptions);

        // Download and save the shelf cover, if available.
        if (shelf.cover != null && shelf.cover.url.IsNotWhite())
        {
            var coverUrl = new Uri(shelf.cover.url);
            var coverFile = shelvesDir.RelativeFile($"{shelf.id:D4}S-cover{Path.GetExtension(coverUrl.AbsolutePath)}");
            await context.Http.GetFileAsync(coverUrl, coverFile.FullName, context.CancelToken);
        }
    }

    WriteLine($"Completed");

});

record ExportContext(BookStackClientHelper Helper, HttpClient Http, JsonSerializerOptions JsonOptions, DirectoryInfo ExportDir, CancellationToken CancelToken);

ShelfMetadata createMetadata(ReadShelfResult shelf, ContentPermissionsItem permissions)
    => new(
        shelf.id, shelf.name, shelf.slug, shelf.description_html,
        shelf.books.Select(b => b.id).ToArray(),
        shelf.created_at, shelf.updated_at,
        shelf.created_by, shelf.updated_by, shelf.owned_by,
        shelf.tags, shelf.cover, permissions
    );

BookMetadata createMetadata(ReadBookResult book, ContentPermissionsItem permissions)
    => new(
        book.id, book.name, book.slug, book.description_html, book.default_template_id,
        book.created_at, book.updated_at,
        book.created_by, book.updated_by, book.owned_by,
        book.tags, book.cover, permissions
    );

ChapterMetadata createMetadata(ReadChapterResult chapter, ContentPermissionsItem permissions)
    => new(
        chapter.id, chapter.name, chapter.slug, chapter.description_html, chapter.priority,
        chapter.created_at, chapter.updated_at,
        chapter.created_by, chapter.updated_by, chapter.owned_by,
        chapter.tags, permissions
    );

PageMetadata createMetadata(ReadPageResult page, ContentPermissionsItem permissions)
    => new(
        page.id, page.name, page.slug, page.priority,
        page.editor, page.revision_count, page.draft, page.template,
        page.created_at, page.updated_at,
        page.created_by, page.updated_by, page.owned_by,
        page.tags, permissions
    );

async ValueTask exportPageAsync(ExportContext context, DirectoryInfo baseDir, long pageId)
{
    // Read page information
    var page = await context.Helper.Try((c, _) => c.ReadPageAsync(pageId, context.CancelToken));
    var pagePerms = await context.Helper.Try((c, _) => c.ReadPagePermissionsAsync(pageId, context.CancelToken));

    // Save page information
    var pageDir = baseDir.RelativeDirectory($"{page.priority:D4}P.{page.name.ToFileName()}").WithCreate();
    var pageMeta = createMetadata(page, pagePerms);
    await pageDir.RelativeFile("page-meta.json").WriteJsonAsync(pageMeta, context.JsonOptions);

    // Save the page content corresponding to the editor.
    if (page.markdown.IsNotWhite())
    {
        await pageDir.RelativeFile("page-content.md").WriteAllTextAsync(page.markdown);
    }
    else
    {
        await pageDir.RelativeFile("page-content.html").WriteAllTextAsync(page.raw_html);
    }

    // Save page attachments
    var attachDir = pageDir.RelativeDirectory("attachments");
    await foreach (var attachInfo in context.Helper.EnumeratePageAttachmentsAsync(pageId))
    {
        // Obtain attachment information.
        var attach = await context.Helper.Try((c, _) => c.ReadAttachmentAsync(attachInfo.id, context.CancelToken));

        // Save attachment information
        await attachDir.WithCreate().RelativeFile($"{attach.id:D4}A.attach-meta.json").WriteJsonAsync(attach, context.JsonOptions);
    }

    // Save page images
    var imageDir = pageDir.RelativeDirectory("images");
    await foreach (var imageInfo in context.Helper.EnumeratePageImagesAsync(pageId))
    {
        // Obtain image information.
        var image = await context.Helper.Try((c, _) => c.ReadImageAsync(imageInfo.id, context.CancelToken));
        await imageDir.WithCreate().RelativeFile($"{image.id:D4}I.image-meta.json").WriteJsonAsync(image, context.JsonOptions);

        // Save image file
        var imageFile = imageDir.RelativeFile($"{image.id:D4}I.{Path.GetFileName(image.path)}");
        await context.Http.GetFileAsync(new(image.url), imageFile, context.CancelToken);
    }

}
