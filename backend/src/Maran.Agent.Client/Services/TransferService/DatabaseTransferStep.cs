namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The steps of a database transfer, in the order they run.</summary>
public enum DatabaseTransferStep
{
    /// <summary>Dumping the database on the source into a file on the target host.</summary>
    Dumping = 1,

    /// <summary>Loading the dump through the restricted loader.</summary>
    Loading = 2,
}
