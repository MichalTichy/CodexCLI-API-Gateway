using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodexGateway.Logic.Configuration;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Security;

public sealed record GlobalApiKeyIdentity(string Id, string Name);
