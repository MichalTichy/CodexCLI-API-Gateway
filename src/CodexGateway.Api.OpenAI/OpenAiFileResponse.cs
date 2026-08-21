using CodexGateway.Models;

namespace CodexGateway.Api.OpenAI;

public sealed record OpenAiFileResponse
{
    public OpenAiFileResponse(FileRecord file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Id = file.Id;
        Bytes = file.Bytes;
        CreatedAt = file.CreatedAt.ToUnixTimeSeconds();
        Filename = file.FileName;
        Purpose = file.Purpose;
    }

    public string Id { get; }

    public string Object => "file";

    public long Bytes { get; }

    public long CreatedAt { get; }

    public string Filename { get; }

    public string Purpose { get; }
}
