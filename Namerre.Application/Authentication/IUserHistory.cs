namespace Namerre.Application.Authentication;

/// <summary>Rename history attributed to users (<c>CreatedBy</c> and <c>RecoveredBy</c> on plans and runs).</summary>
public interface IUserHistory
{
    /// <summary>Attributes everything recorded for <paramref name="fromUserId"/> to <paramref name="to"/>; returns
    /// the number of plans and runs changed.</summary>
    Task<int> Reattribute(long fromUserId, UserReference to, CancellationToken cancellationToken);
}
