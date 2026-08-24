namespace Shared.Infrastructure.LeaderElection;

public interface ILeaderElection
{
    Task<bool> CheckIfCurrentInstanceIsLeaderAsync(bool giveOthersChanceToBecomeLeaderFirst = false);

    Task<bool> WaitForLeaderToBeReadyAsync(TimeSpan timeout);

    void InitReElection();
}
