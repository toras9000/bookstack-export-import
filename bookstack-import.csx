#r "nuget: Lestaly, 0.56.0"
#load ".bookstack-api-helper.csx"
#load ".bookstack-data.csx"
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
    Options = new
    {
        // Whether to import only for empty instance.
        OnlyEmptyInstance = true,

        // Whether or not to skip importing when a book with the same title exists.
        // This is only determined for books existing in the import destination instance. Even if there are multiple identical titles in the import data, they will not be skipped.
        SkipSameTitle = true,
    },

    BookStack = new
    {
        // Target BookStack service address
        ServiceUrl = new Uri("http://localhost:9972/"),

        // API token of the user performing the export
        ApiToken = "444455556666777788889999aaaabbbb",

        // API secret of the user performing the export
        ApiSecret = "ccccddddeeeeffff0000111122223333",
    },

};

return await Paved.RunAsync(config: c => c.AnyPause(), action: async () =>
{
    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = ConsoleWig.CreateCancelKeyHandlePeriod();

    // Title display
    Console.WriteLine($"Importing data into BookStack");
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

    // Have the user enter the location of the data to be captured.
    ConsoleWig.WriteLine("Specify the directory for the imported data.").Write(">");
    var importInput = ConsoleWig.ReadLine().CancelIfWhite();
    var importDataDir = CurrentDir.RelativeDirectory(importInput);

    // Reads information from imported data
    if (!importDataDir.Exists) throw new PavedMessageException("ImportImport data directory does not exist.", PavedMessageKind.Warning);
    var exportMeta = await importDataDir.RelativeFile("export-meta.json").ReadJsonAsync<ExportMetadata>(signal.Token) ?? throw new PavedMessageException("Information is not readable when exporting.");
    if (!BookStackVersion.TryParse(exportMeta.version, out var dataVersion)) throw new PavedMessageException("Unable to recognize the version of imported data.");
    if (dataVersion < version) throw new PavedMessageException("Importing into versions older than the original is not supported.", PavedMessageKind.Warning);

    // Create context instance
    var context = new ImportContext(helper, importDataDir, signal.Token);

    // If the setting is to run only on an empty instance, check for the existence of a book.
    if (settings.Options.OnlyEmptyInstance)
    {
        var books = await context.Helper.Try(s => s.ListBooksAsync(new(count: 1), context.CancelToken));
        if (0 < books.data.Length) throw new PavedMessageException("Cancelled due to the existence of a book in the instance to be imported.", PavedMessageKind.Warning);
    }

    // When performing same title skipping, perform acquisition of all existing book titles.
    var bookTitles = new HashSet<string>();
    if (settings.Options.SkipSameTitle)
    {
        bookTitles = await getAllBookTitles(context);
    }

    // Enumeration options for single-level searches
    var oneLvEnum = new EnumerationOptions();
    oneLvEnum.RecurseSubdirectories = false;
    oneLvEnum.MatchType = MatchType.Simple;
    oneLvEnum.MatchCasing = MatchCasing.PlatformDefault;
    oneLvEnum.ReturnSpecialDirectories = false;
    oneLvEnum.IgnoreInaccessible = true;

    // Enumerate the directory of book information to be imported
    foreach (var bookDir in context.ImportDir.EnumerateDirectories("*B.*", oneLvEnum))
    {
        // Read the book's meta information file.
        var bookMetaFile = bookDir.RelativeFile("book-meta.json");
        if (!bookMetaFile.Exists) continue;
        var bookMeta = await bookMetaFile.ReadJsonAsync<BookMetadata>(context.CancelToken);
        if (bookMeta == null) continue;

        // Indicate the status.
        Console.WriteLine($"Importing book: {Chalk.Green[bookMeta.name]} ...");

        // Determine if it is subject to skipping by the same title.
        if (settings.Options.SkipSameTitle && bookTitles.Contains(bookMeta.name))
        {
            Console.WriteLine($"  Skip due to the existence of the same title book.");
            continue;
        }

        // Create the file information for the book cover file, if any.
        var bookCoverFile = default(FileInfo);
        if (bookMeta.cover != null && bookMeta.cover.path.IsNotWhite())
        {
            bookCoverFile = bookDir.RelativeFile($"book-cover{Path.GetExtension(bookMeta.cover.path)}").OmitNotExists();
        }

        // Create book
        var book = await context.Helper.Try(s => s.CreateBookAsync(new(bookMeta.name, description_html: bookMeta.description, tags: bookMeta.tags), bookCoverFile?.FullName, cancelToken: context.CancelToken));

        // Search and import content directories.
        foreach (var contentDir in bookDir.EnumerateDirectories("*.*", oneLvEnum).OrderBy(d => d.Name))
        {
            // Determine the type of content.
            if (contentDir.RelativeFile("page-meta.json").OmitNotExists() is FileInfo bookPageMetaFile)
            {
                // Page metadata available. Read page meta information.
                var pageMeta = await bookPageMetaFile.ReadJsonAsync<PageMetadata>(context.CancelToken);
                if (pageMeta == null) continue;

                // Create a page in the book.
                Console.WriteLine($"  Page: {Chalk.Green[pageMeta.name]} ...");
                var page = await importPageAsync(context, contentDir, pageMeta, new("", book_id: book.id));
            }
            else if (contentDir.RelativeFile("chapter-meta.json").OmitNotExists() is FileInfo chapterMetaFile)
            {
                // Chapter metadata available. Read chapter meta information.
                var chapterMeta = await chapterMetaFile.ReadJsonAsync<ChapterMetadata>(context.CancelToken);
                if (chapterMeta == null) continue;

                // Create chapter.
                Console.WriteLine($"  Chapter: {Chalk.Green[chapterMeta.name]} ...");
                var chapter = await context.Helper.Try(c => c.CreateChapterAsync(new(book.id, chapterMeta.name, description_html: chapterMeta.description, tags: chapterMeta.tags), context.CancelToken));

                // Importing pages in chapter
                foreach (var pageDir in contentDir.EnumerateDirectories("*P.*", oneLvEnum).OrderBy(d => d.Name))
                {
                    // Page metadata file check
                    var chapterPageMetaFile = pageDir.RelativeFile("page-meta.json");
                    if (!chapterPageMetaFile.Exists) continue;

                    // Read page meta information
                    var pageMeta = await chapterPageMetaFile.ReadJsonAsync<PageMetadata>(context.CancelToken);
                    if (pageMeta == null) continue;

                    // Create pages in chapter.
                    Console.WriteLine($"    Page: {Chalk.Green[pageMeta.name]} ...");
                    var page = await importPageAsync(context, pageDir, pageMeta, new("", chapter_id: chapter.id));
                }
            }
        }
    }

    Console.WriteLine($"Completed");

});

record ImportContext(BookStackClientHelper Helper, DirectoryInfo ImportDir, CancellationToken CancelToken);

async ValueTask<HashSet<string>> getAllBookTitles(ImportContext context)
{
    var offset = 0;
    var titles = new HashSet<string>();
    while (true)
    {
        var books = await context.Helper.Try(c => c.ListBooksAsync(new(offset, count: 500), context.CancelToken));
        foreach (var book in books.data)
        {
            titles.Add(book.name);
        }

        // Update search information and determine end of search.
        offset += books.data.Length;
        var finished = (books.data.Length <= 0) || (books.total <= offset);
        if (finished) break;
    }

    return titles;
}

//
async ValueTask<PageItem?> importPageAsync(ImportContext context, DirectoryInfo pageDir, PageMetadata pageMeta, CreatePageArgs baseArgs)
{
    // Generate page creation arguments.
    var createArgs = default(CreatePageArgs);
    if (pageMeta.editor == "markdown")
    {
        // If the page content is in Markdown format.
        var contentFile = pageDir.RelativeFile("page-content.md");
        if (!contentFile.Exists) return null;
        createArgs = baseArgs with { name = pageMeta.name, tags = pageMeta.tags, markdown = await contentFile.ReadAllTextAsync(context.CancelToken), };
    }
    else
    {
        // If the page content is in HTML format
        var contentFile = pageDir.RelativeFile("page-content.html");
        if (!contentFile.Exists) return null;
        createArgs = baseArgs with { name = pageMeta.name, tags = pageMeta.tags, html = await contentFile.ReadAllTextAsync(context.CancelToken), };
    }

    // Create page
    var page = await context.Helper.Try(s => s.CreatePageAsync(createArgs, context.CancelToken));

    // Enumeration options for single-level searches
    var oneLvEnum = new EnumerationOptions();
    oneLvEnum.RecurseSubdirectories = false;
    oneLvEnum.MatchType = MatchType.Simple;
    oneLvEnum.MatchCasing = MatchCasing.PlatformDefault;
    oneLvEnum.ReturnSpecialDirectories = false;
    oneLvEnum.IgnoreInaccessible = true;

    // Importing Attachments
    var attachDir = pageDir.RelativeDirectory("attachments");
    if (attachDir.Exists)
    {
        foreach (var attachMetaFile in attachDir.EnumerateFiles("*.attach-meta.json", oneLvEnum).OrderBy(d => d.Name))
        {
            // Read attachment meta information
            var attachMeta = await attachMetaFile.ReadJsonAsync<ReadAttachmentResult>(context.CancelToken);
            if (attachMeta == null) continue;

            // Attachment according to attachment type
            if (attachMeta.external)
            {
                // Attach external links
                var attachment = await context.Helper.Try(s => s.CreateLinkAttachmentAsync(new(attachMeta.name, page.id, attachMeta.content), context.CancelToken));
            }
            else
            {
                // Attach file
                var attachBin = attachMeta.content.DecodeBase64();
                if (attachBin == null) continue;
                var attachName = $"{attachMeta.name}.{attachMeta.extension}";
                var attachment = await context.Helper.Try(s => s.CreateFileAttachmentAsync(new(attachMeta.name, page.id), attachBin, attachName, context.CancelToken));
            }
        }
    }

    // Importing Images
    var imageDir = pageDir.RelativeDirectory("images");
    if (imageDir.Exists)
    {
        foreach (var imageMetaFile in imageDir.EnumerateFiles("*.image-meta.json", oneLvEnum).OrderBy(d => d.Name))
        {
            // Read image meta information
            var imageMeta = await imageMetaFile.ReadJsonAsync<ImageItem>(context.CancelToken);
            if (imageMeta == null) continue;

            // Identify the name of the image file to be imported.
            var imageFile = imageMetaFile.RelativeFile($"{imageMeta.id:D4}I.{Path.GetFileName(imageMeta.path)}");
            if (!imageFile.Exists) continue;

            // Create an image
            var image = await context.Helper.Try(s => s.CreateImageAsync(new(page.id, imageMeta.type, imageMeta.name), imageFile.FullName, cancelToken: context.CancelToken));

        }
    }

    return page;
}