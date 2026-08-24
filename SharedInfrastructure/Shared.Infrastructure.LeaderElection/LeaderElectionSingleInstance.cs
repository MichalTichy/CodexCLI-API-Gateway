namespace Shared.Infrastructure.LeaderElection;

public sealed class LeaderElectionSingleInstance : ILeaderElection
{
    public Task<bool> CheckIfCurrentInstanceIsLeaderAsync(
        bool giveOthersChanceToBecomeLeaderFirst = false) =>
        Task.FromResult(true);

    public Task<bool> WaitForLeaderToBeReadyAsync(TimeSpan timeout) =>
        Task.FromResult(true);

    public void InitReElection()
    {
    }
}
