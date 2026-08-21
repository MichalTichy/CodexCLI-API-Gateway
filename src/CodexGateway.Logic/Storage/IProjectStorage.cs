using System.Text.Json;
using CodexGateway.Logic.Specifications;
using CodexGateway.Models;

namespace CodexGateway.Logic.Storage;

public interface IProjectStorage
{
    void Create(string projectId);

    void Delete(string projectId);
}
