using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.LeaderElection;

namespace Shared.Infrastructure.Initializer;

public sealed class InitializerManager(
    ILogger<InitializerManager> logger,
    IServiceProvider serviceProvider,
    ILeaderElection leaderElection,
    IConfiguration configuration)
{
    private static readonly SemaphoreSlim InitializersRunSemaphore = new(1);
    private readonly TaskCompletionSource<bool> _initializersFinished = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsInitialized => HaveAllInitializationPhasesFinished();

    public ICollection<InitializerTrigger> ExecutedTriggers { get; } = [];

    public async Task RunAllInitializersAsync(
        InitializerTrigger trigger,
        params InitializerTrigger[] additionalTriggers)
    {
        var triggers = additionalTriggers.Prepend(trigger).ToArray();
        CheckThatInitializersWereNotAlreadyRun(triggers);

        await InitializersRunSemaphore.WaitAsync();
        try
        {
            CheckThatInitializersWereNotAlreadyRun(triggers);
            foreach (var triggerToAdd in triggers)
            {
                ExecutedTriggers.Add(triggerToAdd);
            }

            using var scope = serviceProvider.CreateScope();
            var allInitializers = scope.ServiceProvider.GetServices<IInitializer>();
            var isLeader = await WaitToBecomeLeaderOrForLeaderToBeReadyAsync();
            if (!isLeader)
            {
                allInitializers = allInitializers.Where(initializer => !initializer.RunOnlyInLeaderInstance);
            }

            var selectedInitializers = allInitializers
                .Where(initializer => triggers.Contains(initializer.Trigger))
                .OrderBy(initializer => initializer.Trigger)
                .ThenByDescending(initializer => initializer.Priority)
                .ToArray();

            logger.LogInformation("Running {InitializerCount} initializers.", selectedInitializers.Length);
            foreach (var initializer in selectedInitializers)
            {
                logger.LogInformation(
                    "Running initializer {InitializerName} - priority {Priority}.",
                    initializer.Name,
                    initializer.Priority);
                await initializer.InitializeAsync(configuration);
                logger.LogInformation("Initializer {InitializerName} finished.", initializer.Name);
            }

            leaderElection.InitReElection();
            if (HaveAllInitializationPhasesFinished())
            {
                _initializersFinished.TrySetResult(true);
            }
        }
        catch (Exception exception)
        {
            _initializersFinished.TrySetException(exception);
            throw;
        }
        finally
        {
            InitializersRunSemaphore.Release();
        }
    }

    public Task WaitForAllInitializersToFinishAsync() => _initializersFinished.Task;

    private async Task<bool> WaitToBecomeLeaderOrForLeaderToBeReadyAsync()
    {
        while (true)
        {
            var isLeader = await leaderElection.CheckIfCurrentInstanceIsLeaderAsync(true);
            if (isLeader)
            {
                return true;
            }

            logger.LogInformation("Waiting for the leader instance to finish initialization.");
            if (await leaderElection.WaitForLeaderToBeReadyAsync(TimeSpan.FromSeconds(2)))
            {
                return false;
            }

            logger.LogWarning("The leader instance is not ready yet.");
        }
    }

    private bool HaveAllInitializationPhasesFinished() =>
        Enum.GetValues<InitializerTrigger>().All(ExecutedTriggers.Contains);

    private void CheckThatInitializersWereNotAlreadyRun(InitializerTrigger[] triggers)
    {
        foreach (var trigger in triggers)
        {
            if (ExecutedTriggers.Contains(trigger))
            {
                throw new NotSupportedException(
                    $"Initializers with trigger {trigger} have already run.");
            }
        }
    }
}
