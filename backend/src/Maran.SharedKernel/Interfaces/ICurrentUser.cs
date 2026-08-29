namespace Maran.SharedKernel.Interfaces;

/// <summary>The authenticated principal of the current request/message.</summary>
public interface ICurrentUser
{
    /// <summary>Panel user id.</summary>
    Guid UserId { get; }

    /// <summary>Owning account for Customer contexts; null for Admin.</summary>
    Guid? AccountId { get; }

    /// <summary>True for server administrators.</summary>
    bool IsAdmin { get; }
}
