namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>What an administrator asks of a whole job.</summary>
public enum MigrationJobAction
{
    /// <summary>Start the engine on a mapped job.</summary>
    Start,

    /// <summary>Stop every running transfer at its next boundary and start no other.</summary>
    Pause,

    /// <summary>Continue a paused job from the partial state.</summary>
    Resume,

    /// <summary>End the job and delete the accounts it created.</summary>
    Cancel,
}
