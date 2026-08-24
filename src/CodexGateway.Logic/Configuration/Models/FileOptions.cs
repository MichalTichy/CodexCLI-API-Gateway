using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration.Models;

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
        ".txt", ".md", ".rst", ".rtf", ".log", ".tex", ".json", ".jsonl", ".yaml", ".yml",
        ".toml", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".xml", ".html", ".css",
        ".js", ".ts", ".jsx", ".tsx", ".cs", ".fs", ".vb", ".py", ".java", ".go", ".rs", ".sql",

        ".pdf", ".epub",
        ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm",
        ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm",
        ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".ppsm", ".pot", ".potx", ".potm",
        ".one", ".pub", ".vsd", ".vsdx", ".vsdm",
        ".odt", ".ott", ".ods", ".ots", ".odp", ".otp", ".odg",
        ".pages", ".numbers", ".key",

        ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".gif", ".webp", ".avif", ".jxl",
        ".bmp", ".dib", ".tif", ".tiff", ".heic", ".heif", ".ico", ".cur",
        ".svg", ".svgz", ".wmf", ".emf", ".eps", ".ai",
        ".psd", ".psb", ".tga", ".dds", ".exr", ".hdr",
        ".jp2", ".j2k", ".jpf", ".jpx", ".jpm", ".mj2",
        ".pbm", ".pgm", ".ppm", ".pnm",
        ".raw", ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2",
        ".orf", ".rw2", ".raf", ".pef", ".x3f", ".3fr", ".erf", ".kdc", ".mef",
        ".mos", ".mrw", ".rwl", ".srw"
    ];
}
