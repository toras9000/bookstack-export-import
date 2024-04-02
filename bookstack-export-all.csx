#r "nuget: Lestaly, 0.58.0"
#load "modules/.bookstack-api-helper.csx"
#load "modules/.bookstack-data.csx"
#nullable enable
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BookStackApiClient;
using Kokuban;
using Lestaly;

var settings = new
{
    BookStack = new
    {
        // Target BookStack service address
        ServiceUrl = new Uri("http://localhost:9971/"),

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

return await Paved.RunAsync(config: c => c.AnyPause(), action: async () =>
{
    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = ConsoleWig.CreateCancelKeyHandlePeriod();

    // Title display
    Console.WriteLine($"Exporting data from BookStack");
    Console.WriteLine($"  Service Address: {settings.BookStack.ServiceUrl}");
    Console.WriteLine();

    // Create client and helper
    var apiUri = new Uri(settings.BookStack.ServiceUrl, "/api/");
    var apiKey = new ApiKey(settings.BookStack.ApiToken, settings.BookStack.ApiSecret);
    using var helper = new BookStackClientHelper(apiUri, apiKey, cancelToken: signal.Token);

    // Detect BookStack version
    var version = await helper.DetectBookStackVersionAsync(settings.BookStack.ServiceUrl);
    if (version == null) throw new PavedMessageException("Unable to detect BookStack version.", PavedMessageKind.Warning);
    if (version < new BookStackVersion("23.12.0")) throw new PavedMessageException("Unsupported BookStack version.", PavedMessageKind.Warning);

    // Obtain information about myself. This may not be obtained.
    var user_me = await helper.GetMeAsync();

    // Determine output directory
    var exportTime = DateTime.Now;
    var exportDir = settings.Local.ExportDir.RelativeDirectory($"{exportTime:yyyy.MM.dd-HH.mm.ss}").WithCreate();
    Console.WriteLine($"Export to {exportDir.FullName}");
    Console.WriteLine();

    // Options for saving JSON
    var jsonOptions = new JsonSerializerOptions();
    jsonOptions.WriteIndented = true;

    // Create context instance
    var context = new ExportContext(helper, jsonOptions, exportDir, signal.Token);

    // Output export information
    var exportMeta = new ExportMetadata(settings.BookStack.ServiceUrl.AbsoluteUri, version.OriginalString, exportTime, user_me);
    await exportDir.RelativeFile("export-meta.json").WriteJsonAsync(exportMeta, jsonOptions);

    // Retrieve information for each book
    await foreach (var summary in context.Helper.EnumerateAllBooksAsync())
    {
        // Indicate the status.
        Console.WriteLine($"Exporting book: {Chalk.Green[summary.name]} ...");

        // Read book information
        var book = await context.Helper.Try(c => c.ReadBookAsync(summary.id, context.CancelToken));
        var bookPerms = await context.Helper.Try(c => c.ReadBookPermissionsAsync(summary.id, context.CancelToken));

        // Save book information
        var bookDir = exportDir.RelativeDirectory($"{book.id:D4}B.{book.name.ToFileName()}").WithCreate();
        var bookMeta = createMetadata(book, bookPerms);
        await bookDir.RelativeFile("book-meta.json").WriteJsonAsync(bookMeta, context.JsonOptions);

        // Download and save the book cover, if available.
        if (book.cover != null && book.cover.url.IsNotWhite())
        {
            var coverUrl = new Uri(book.cover.url);
            var coverFile = bookDir.RelativeFile($"book-cover{Path.GetExtension(coverUrl.AbsolutePath)}");
            await context.Helper.Http.GetFileAsync(coverUrl, coverFile.FullName, context.CancelToken);
        }

        // Save Book Contents
        foreach (var content in book.contents.OrderBy(c => c.priority))
        {
            // Save process according to content
            if (content is BookContentChapter chapterContent)
            {
                // Read chapter information
                Console.WriteLine($"  Chapter: {Chalk.Green[chapterContent.name]} ...");
                var chapter = await context.Helper.Try(c => c.ReadChapterAsync(chapterContent.id, context.CancelToken));
                var chapterPerms = await context.Helper.Try(c => c.ReadChapterPermissionsAsync(chapterContent.id, context.CancelToken));

                // Save chapter information
                var chapterDir = bookDir.RelativeDirectory($"{chapter.priority:D4}C.{chapter.name.ToFileName()}").WithCreate();
                var chapterMeta = createMetadata(chapter, chapterPerms);
                await chapterDir.RelativeFile("chapter-meta.json").WriteJsonAsync(chapterMeta, context.JsonOptions);

                // Save each page in a chapter
                foreach (var pageContent in chapter.pages)
                {
                    Console.WriteLine($"    Page: {Chalk.Green[pageContent.name]} ...");
                    await exportPageAsync(context, chapterDir, pageContent.id);
                }
            }
            else if (content is BookContentPage pageContent)
            {
                // Save page
                Console.WriteLine($"  Page: {Chalk.Green[pageContent.name]} ...");
                await exportPageAsync(context, bookDir, pageContent.id);
            }
        }
    }

    Console.WriteLine($"Completed");

});

record ExportContext(BookStackClientHelper Helper, JsonSerializerOptions JsonOptions, DirectoryInfo ExportDir, CancellationToken CancelToken);

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
    var page = await context.Helper.Try(c => c.ReadPageAsync(pageId, context.CancelToken));
    var pagePerms = await context.Helper.Try(c => c.ReadPagePermissionsAsync(pageId, context.CancelToken));

    // Save page information
    var pageDir = baseDir.RelativeDirectory($"{page.priority:D4}P.{page.name.ToFileName()}").WithCreate();
    var pageMeta = createMetadata(page, pagePerms);
    await pageDir.RelativeFile("page-meta.json").WriteJsonAsync(pageMeta, context.JsonOptions);

    // Save the page content corresponding to the editor.
    if (page.editor == "markdown")
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
        var attach = await context.Helper.Try(c => c.ReadAttachmentAsync(attachInfo.id, context.CancelToken));

        // Save attachment information
        await attachDir.WithCreate().RelativeFile($"{attach.id:D4}A.attach-meta.json").WriteJsonAsync(attach, context.JsonOptions);
    }

    // Save page images
    var imageDir = pageDir.RelativeDirectory("images");
    await foreach (var imageInfo in context.Helper.EnumeratePageImagesAsync(pageId))
    {
        // Obtain image information.
        var image = await context.Helper.Try(c => c.ReadImageAsync(imageInfo.id, context.CancelToken));
        await imageDir.WithCreate().RelativeFile($"{image.id:D4}I.image-meta.json").WriteJsonAsync(image, context.JsonOptions);

        // Save image file
        var imageFile = imageDir.RelativeFile($"{image.id:D4}I.{Path.GetFileName(image.path)}");
        await context.Helper.Http.GetFileAsync(new(image.url), imageFile, context.CancelToken);
    }

}
