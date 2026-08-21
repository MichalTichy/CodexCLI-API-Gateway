using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class FileOptions
{
    [Range(1, 1024)]
    public int MaxUploadMegabytes { get; set; } = 25;

    [Range(1, 168)]
    public int ProjectlessTtlHours { get; set; } = 24;

    public string[] AllowedExtensions { get; set; } =
    [
        ".txt", ".md", ".json", ".yaml", ".yml", ".csv", ".xml", ".html", ".css",
        ".js", ".ts", ".jsx", ".tsx", ".cs", ".fs", ".vb", ".py", ".java", ".go",
        ".rs", ".sql", ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp"
    ];
}
