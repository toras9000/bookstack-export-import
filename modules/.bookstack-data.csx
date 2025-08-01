#r "nuget: BookStackApiClient, 25.5.0-lib.3"
#nullable enable
using BookStackApiClient;

record ExportMetadata(string service_url, BookStackVersion version, DateTime export_at, User export_by);

record ShelfMetadata(
    long id, string name, string slug, string description, long[] books,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags, ShelfCover? cover, ContentPermissionsItem permissions
);

record BookMetadata(
    long id, string name, string slug, string description, long? default_template_id,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags, BookCover? cover, ContentPermissionsItem permissions
);

record ChapterMetadata(
    long id, string name, string slug, string description, long priority,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags, ContentPermissionsItem permissions
);

record PageMetadata(
    long id, string name, string slug, long priority,
    string editor, long revision_count, bool draft, bool template,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags, ContentPermissionsItem permissions
);
