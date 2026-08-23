using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class FileOptions
{
    /// <summary>
    /// Maximum size, in megabytes, accepted for one uploaded file.
    /// Larger request bodies or files are rejected before being added to storage.
    /// </summary>
    [Range(1, 1024)]
    public int MaxUploadMegabytes { get; set; } = 25;

    /// <summary>
    /// Number of hours an upload without a project remains available to the API key that created it.
    /// After this period it is treated as expired and the background cleanup removes its content and metadata.
    /// Project-owned files are not affected.
    /// </summary>
    [Range(1, 168)]
    public int ProjectlessTtlHours { get; set; } = 24;

    /// <summary>
    /// Case-insensitive allowlist of file-name extensions accepted for uploads, including the leading dot.
    /// An upload with an extension outside this list is rejected.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".txt", ".md", ".json", ".yaml", ".yml", ".csv", ".xml", ".html", ".css",
        ".js", ".ts", ".jsx", ".tsx", ".cs", ".fs", ".vb", ".py", ".java", ".go",
        ".rs", ".sql", ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp"
    ];
}
