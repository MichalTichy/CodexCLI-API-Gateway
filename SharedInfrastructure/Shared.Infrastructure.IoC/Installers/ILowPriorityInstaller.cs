using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shared.Infrastructure.IoC.Installers;

public interface ILowPriorityInstaller : IInstaller;
