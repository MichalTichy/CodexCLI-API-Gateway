using System.Text.Json;

namespace CodexGateway.Logic.Generation.Models;

public sealed record FileContentPart(string FileId) : InputContentPart;
