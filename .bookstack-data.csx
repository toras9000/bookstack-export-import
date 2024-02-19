#r "nuget: BookStackApiClient, 23.12.1-lib.1"
#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using BookStackApiClient;

record ExportMetadata(string service_url, string version, DateTime export_at, User export_by);

record BookMetadata(
    long id, string name, string slug, string description, long? default_template_id,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags, BookCover? cover
);

record ChapterMetadata(
    long id, string name, string slug, string description, long priority,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags
);

record PageMetadata(
    long id, string name, string slug, long priority,
    string editor, long revision_count, bool draft, bool template,
    DateTime created_at, DateTime updated_at,
    User created_by, User updated_by, User owned_by,
    ContentTag[]? tags
);
