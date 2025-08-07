#r "nuget: R3, 1.3.0"
#r "nuget: Kokuban, 0.2.0"
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
using R3;

var settings = new
{
    Options = new
    {
        // Whether to import only for empty instance.
        OnlyEmptyInstance = true,

        // Whether or not to skip importing when a book with the same title exists.
        // This is only determined for books existing in the import destination instance. Even if there are multiple identical titles in the import data, they will not be skipped.
        SkipSameTitle = true,

        // Whether to restore permissions or not
        // If there is no user or role with the same name at the import destination, the restoration is ignored.
        RestorePermissions = true,
    },

    BookStack = new
    {
        // Target BookStack service address
        ServiceUrl = new Uri("http://localhost:8821/"),

        // API token of the user performing the export
        ApiToken = "444455556666777788889999aaaabbbb",

        // API secret of the user performing the export
        ApiSecret = "ccccddddeeeeffff0000111122223333",
    },

};

return await Paved.ProceedAsync(async () =>
{
    // Prepare console
    using var outenc = ConsoleWig.OutputEncodingPeriod(Encoding.UTF8);
    using var signal = new SignalCancellationPeriod();

    // Title display
    WriteLine($"Importing data into BookStack");
    WriteLine($"  Service Address: {settings.BookStack.ServiceUrl}");
    WriteLine();

    // Create client and helper
    var apiUri = new Uri(settings.BookStack.ServiceUrl, "/api/");
    var apiKey = new ApiKey(settings.BookStack.ApiToken, settings.BookStack.ApiSecret);
    using var http = new HttpClient();
    using var client = new BookStackClient(apiUri, apiKey.Token, apiKey.Secret);
    using var helper = new BookStackClientHelper(client, signal.Token);
    helper.LimitHandler += (args) =>
    {
        WriteLine(Chalk.Yellow[$"Caught in API call rate limitation. Rate limit: {args.Exception.RequestsPerMin} [per minute], {args.Exception.RetryAfter} seconds to lift the limit."]);
        WriteLine(Chalk.Yellow[$"It will automatically retry after a period of time has elapsed."]);
        WriteLine(Chalk.Yellow[$"[Waiting...]"]);
        return ValueTask.CompletedTask;
    };

    // Detect BookStack version
    var version = await http.DetectBookStackVersionAsync(settings.BookStack.ServiceUrl);
    if (version == null) throw new PavedMessageException("Unable to detect BookStack version.", PavedMessageKind.Warning);
    if (version < BookStackVersion.Parse("23.12.0")) throw new PavedMessageException("Unsupported BookStack version.", PavedMessageKind.Warning);

    // Have the user enter the location of the data to be captured.
    WriteLine("Specify the directory for the imported data.");
    Write(">");
    var importInput = ReadLine()?.Unquote().CancelIfWhite();
    var importDataDir = CurrentDir.RelativeDirectory(importInput);

    // Reads information from imported data
    if (!importDataDir.Exists) throw new PavedMessageException("ImportImport data directory does not exist.", PavedMessageKind.Warning);
    var exportMeta = await importDataDir.RelativeFile("export-meta.json").ReadJsonAsync<ExportMetadata>(signal.Token) ?? throw new PavedMessageException("Information is not readable when exporting.");
    if (version < exportMeta.version) throw new PavedMessageException("Importing into versions older than the original is not supported.", PavedMessageKind.Warning);

    // Create context instance
    var context = new ImportContext(helper, http, importDataDir, signal.Token);

    // Obtain instance information
    var instance = await scopeAction(async () =>
    {
        // If the setting is to run only on an empty instance, check for the existence of a book.
        if (settings.Options.OnlyEmptyInstance)
        {
            var books = await context.Helper.Try((c, _) => c.ListBooksAsync(new(count: 1), context.CancelToken));
            if (0 < books.data.Length) throw new PavedMessageException("Cancelled due to the existence of a book in the instance to be imported.", PavedMessageKind.Warning);
        }

        // Retrieve all shelf information.
        var shelves = await context.Helper.EnumerateAllShelvesAsync().ToObservable().ToArrayAsync(context.CancelToken);

        // When performing same title skipping, perform acquisition of all existing book titles.
        var bookTitles = default(HashSet<string>);
        if (settings.Options.SkipSameTitle)
        {
            bookTitles = await context.Helper.EnumerateAllBooksAsync().ToObservable().Select(b => b.name).ToHashSetAsync(context.CancelToken);
        }

        // Retrieve user and role information for the import destination instance when restoring permissions.
        var users = default(UserSummary[]);
        var roles = default(RoleSummary[]);
        if (settings.Options.RestorePermissions)
        {
            users = await context.Helper.EnumerateAllUsersAsync().ToObservable().ToArrayAsync(context.CancelToken);
            roles = await context.Helper.EnumerateAllRolesAsync().ToObservable().ToArrayAsync(context.CancelToken);
        }

        return new ImportInstance(version, shelves, bookTitles ?? [], users ?? [], roles ?? []);

    });

    // Enumeration options for single-level searches
    var oneLvEnum = new EnumerationOptions();
    oneLvEnum.RecurseSubdirectories = false;
    oneLvEnum.MatchType = MatchType.Simple;
    oneLvEnum.MatchCasing = MatchCasing.PlatformDefault;
    oneLvEnum.ReturnSpecialDirectories = false;
    oneLvEnum.IgnoreInaccessible = true;

    // Read import shelves info
    var importShelves = new List<ShelfMapInfo>();
    var shelvesDir = context.ImportDir.RelativeDirectory("shelves");
    if (shelvesDir.Exists)
    {
        foreach (var shelfMetaFile in shelvesDir.EnumerateFiles("*S-meta.json", oneLvEnum) ?? [])
        {
            var shelfMeta = await shelfMetaFile.ReadJsonAsync<ShelfMetadata>(context.CancelToken);
            if (shelfMeta == null) continue;
            var shelfCover = shelvesDir.RelativeFile($"{shelfMeta.id:D4}S-cover{Path.GetExtension(shelfMeta.cover?.url)}").OmitNotExists();
            importShelves.Add(new(new(shelfMeta, shelfCover), shelfMeta.books.ToHashSet(), new()));
        }
    }

    // Enumerate the directory of book information to be imported
    foreach (var bookDir in context.ImportDir.EnumerateDirectories("*B.*", oneLvEnum))
    {
        // Read the book's meta information file.
        var bookMetaFile = bookDir.RelativeFile("book-meta.json");
        if (!bookMetaFile.Exists) continue;
        var bookMeta = await bookMetaFile.ReadJsonAsync<BookMetadata>(context.CancelToken);
        if (bookMeta == null) continue;

        // Indicate the status.
        WriteLine($"Importing book: {Chalk.Green[bookMeta.name]} ...");

        // Determine if it is subject to skipping by the same title.
        if (instance.BookTitles?.Contains(bookMeta.name) == true)
        {
            WriteLine($"  Skip due to the existence of the same title book.");
            continue;
        }

        // Create the file information for the book cover file, if any.
        var bookCoverFile = default(FileInfo);
        if (bookMeta.cover != null && bookMeta.cover.path.IsNotWhite())
        {
            bookCoverFile = bookDir.RelativeFile($"book-cover{Path.GetExtension(bookMeta.cover.path)}").OmitNotExists();
        }

        // Create book
        var book = await context.Helper.Try((c, _) => c.CreateBookAsync(new(bookMeta.name, description_html: bookMeta.description, tags: bookMeta.tags), bookCoverFile?.FullName, cancelToken: context.CancelToken));

        // Restore book permissions
        if (settings.Options.RestorePermissions && bookMeta.permissions != null)
        {
            await restorePermissionsAsync(context, instance, "book", book.id, bookMeta.permissions, info => WriteLine($"  {Chalk.Yellow[info]}"));
        }

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
                WriteLine($"  Page: {Chalk.Green[pageMeta.name]} ...");
                var page = await importPageAsync(context, contentDir, pageMeta, new("", book_id: book.id), info => WriteLine($"    {Chalk.Yellow[info]}"));
                if (page != null)
                {
                    // Restore page permissions
                    if (settings.Options.RestorePermissions && pageMeta.permissions != null)
                    {
                        await restorePermissionsAsync(context, instance, "page", page.id, pageMeta.permissions, info => WriteLine($"    {Chalk.Yellow[info]}"));
                    }
                }
            }
            else if (contentDir.RelativeFile("chapter-meta.json").OmitNotExists() is FileInfo chapterMetaFile)
            {
                // Chapter metadata available. Read chapter meta information.
                var chapterMeta = await chapterMetaFile.ReadJsonAsync<ChapterMetadata>(context.CancelToken);
                if (chapterMeta == null) continue;

                // Create chapter.
                WriteLine($"  Chapter: {Chalk.Green[chapterMeta.name]} ...");
                var chapter = await context.Helper.Try((c, _) => c.CreateChapterAsync(new(book.id, chapterMeta.name, description_html: chapterMeta.description, tags: chapterMeta.tags), context.CancelToken));

                // Restore chapter permissions
                if (settings.Options.RestorePermissions && chapterMeta.permissions != null)
                {
                    await restorePermissionsAsync(context, instance, "chapter", chapter.id, chapterMeta.permissions, info => WriteLine($"    {Chalk.Yellow[info]}"));
                }

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
                    WriteLine($"    Page: {Chalk.Green[pageMeta.name]} ...");
                    var page = await importPageAsync(context, pageDir, pageMeta, new("", chapter_id: chapter.id), info => WriteLine($"      {Chalk.Yellow[info]}"));
                    if (page != null)
                    {
                        // Restore page permissions
                        if (settings.Options.RestorePermissions && pageMeta.permissions != null)
                        {
                            await restorePermissionsAsync(context, instance, "page", page.id, pageMeta.permissions, info => WriteLine($"      {Chalk.Yellow[info]}"));
                        }
                    }
                }
            }
        }

        // Corresponding shelves to be imported.
        var targetShelf = importShelves.FirstOrDefault(s => s.SourceBooks.Contains(book.id));
        targetShelf?.ImportedBooks.Add(book.id);
    }

    // Shelf Import
    foreach (var shelf in importShelves)
    {
        var meta = shelf.Info.Meta;
        var existShelf = instance.Shelves.FirstOrDefault(s => s.name == meta.name);
        if (existShelf == null)
        {
            // If there is no shelf with the same name, create one and make the book belong to it.
            var args = new CreateShelfArgs(meta.name, description_html: meta.description, tags: meta.tags, books: shelf.ImportedBooks);
            var imgPath = shelf.Info.Cover?.FullName;
            var imgName = Path.GetFileName(meta.cover?.url);
            await context.Helper.Try((c, _) => c.CreateShelfAsync(args, imgPath, imgName, cancelToken: context.CancelToken));

            // Restore book permissions
            if (settings.Options.RestorePermissions && meta.permissions != null)
            {
                await restorePermissionsAsync(context, instance, "bookshelf", meta.id, meta.permissions, info => WriteLine($"  {Chalk.Yellow[info]}"));
            }
        }
        else
        {
            // If there is a shelf with the same name, let the book belong only.
            var currentShelf = await context.Helper.Try((c, _) => c.ReadShelfAsync(existShelf.id, context.CancelToken));
            var newBooks = currentShelf.books.Select(b => b.id).Concat(meta.books).ToArray();
            await context.Helper.Try((c, _) => c.UpdateShelfAsync(existShelf.id, new(books: newBooks), cancelToken: context.CancelToken));
        }
    }

    WriteLine($"Completed");

});

record ImportContext(BookStackClientHelper Helper, HttpClient Http, DirectoryInfo ImportDir, CancellationToken CancelToken);
record ImportInstance(BookStackVersion Version, IReadOnlyList<ShelfSummary> Shelves, IReadOnlySet<string> BookTitles, IReadOnlyList<UserSummary> Users, IReadOnlyList<RoleSummary> Roles);
record ImportShelfInfo(ShelfMetadata Meta, FileInfo? Cover);
record ShelfMapInfo(ImportShelfInfo Info, IReadOnlySet<long> SourceBooks, List<long> ImportedBooks);

ValueTask<TResult> scopeAction<TResult>(Func<ValueTask<TResult>> action) => action();

async ValueTask<PageItem?> importPageAsync(ImportContext context, DirectoryInfo pageDir, PageMetadata pageMeta, CreatePageArgs baseArgs, Action<string>? notify = default)
{
    // Ignore the draft article.
    if (pageMeta.draft)
    {
        notify?.Invoke($"Import skipped due to draft page.");
        return null;
    }

    // Generate page creation arguments.
    var createArgs = default(CreatePageArgs);
    var contentFile = pageDir.RelativeFile("page-content.md");
    if (contentFile.Exists)
    {
        // If the page content is in Markdown format.
        var content = await contentFile.ReadAllTextAsync(context.CancelToken);
        if (content.IsWhite()) { notify?.Invoke($"Import skipped due to empty page content."); return null; }
        createArgs = baseArgs with { name = pageMeta.name, tags = pageMeta.tags, markdown = content, };
    }
    else
    {
        // If the page content is in HTML format
        contentFile = pageDir.RelativeFile("page-content.html");
        if (!contentFile.Exists) { notify?.Invoke($"Import skipped due to missing page content file '{contentFile.Name}'."); return null; }
        var content = await contentFile.ReadAllTextAsync(context.CancelToken);
        if (content.IsWhite()) { notify?.Invoke($"Import skipped due to empty page content."); return null; }
        createArgs = baseArgs with { name = pageMeta.name, tags = pageMeta.tags, html = content, };
    }

    // Create page
    var page = await context.Helper.Try((c, _) => c.CreatePageAsync(createArgs, context.CancelToken));

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
                var attachment = await context.Helper.Try((c, _) => c.CreateLinkAttachmentAsync(new(attachMeta.name, page.id, attachMeta.content), context.CancelToken));
            }
            else
            {
                // Attach file
                var attachBin = attachMeta.content.DecodeBase64();
                if (attachBin == null) continue;
                var attachName = $"{attachMeta.name}.{attachMeta.extension}";
                var attachment = await context.Helper.Try((c, _) => c.CreateFileAttachmentAsync(new(attachMeta.name, page.id), attachBin, attachName, context.CancelToken));
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
            if (!imageFile.Exists)
            {
                notify?.Invoke($"Import skipped due to missing image file '{imageFile.Name}'.");
                continue;
            }

            // Create an image
            var image = await context.Helper.Try((c, _) => c.CreateImageAsync(new(page.id, imageMeta.type, imageMeta.name), imageFile.FullName, cancelToken: context.CancelToken));

        }
    }

    return page;
}

async ValueTask<ContentPermissionsItem> restorePermissionsAsync(ImportContext context, ImportInstance instance, string type, long id, ContentPermissionsItem permissions, Action<string>? notify = default)
{
    var impOwner = instance.Users.Where(u => u.name == permissions.owner.name).ToArray();
    var impOwnerId = default(long?);
    if (impOwner?.Length == 1)
    {
        impOwnerId = impOwner[0].id;
    }
    else
    {
        notify?.Invoke($"Skipping owner restore because user '{permissions.owner.name}' cannot be identified.");
    }

    var rolePerms = new List<RolePermission>();
    foreach (var perm in permissions.role_permissions)
    {
        var impRole = instance.Roles.Where(r => r.display_name == perm.role.display_name).ToArray();
        if (impOwner?.Length == 1)
        {
            rolePerms.Add(new(impRole[0].id, perm.view, perm.create, perm.update, perm.delete));
        }
        else
        {
            notify?.Invoke($"Skipping permission restore because role '{perm.role.display_name}' cannot be identified.");
        }
    }

    return await context.Helper.Try((c, _) => c.UpdateContentPermissionsAsync(type, id, new(impOwnerId, rolePerms.ToArray(), permissions.fallback_permissions), cancelToken: context.CancelToken));
}
