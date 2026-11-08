namespace Maran.Modules.Files.Models;

/// <summary>
/// A file about to be streamed to the caller: what a download query hands the controller. It is a
/// model and never a DTO, because it is never serialised — the controller copies its bytes to the
/// response.
/// </summary>
/// <remarks>
/// Owns the transfer slot and the agent stream behind it, so disposing it ends the agent call and gives
/// the slot back, whether the copy finished, failed or the client went away. Like the command that
/// uploads, it carries a live stream and is meaningful on the local bus only.
/// </remarks>
public sealed class FileDownload : IAsyncDisposable
{
    /// <summary>Copies the file's bytes to a destination.</summary>
    private readonly Func<Stream, CancellationToken, Task> _copyTo;

    /// <summary>Ends the agent stream and gives the transfer slot back.</summary>
    private readonly Func<ValueTask> _dispose;

    /// <summary>Gets the name the response proposes, already cleaned by <c>FilesDownloadNamePolicy</c>.</summary>
    public string FileName { get; }

    /// <summary>Gets the file's size in bytes, as the agent reported it before streaming.</summary>
    public ulong SizeBytes { get; }

    /// <summary>Creates a download.</summary>
    /// <param name="fileName">The proposed name, already cleaned.</param>
    /// <param name="sizeBytes">The file's size in bytes.</param>
    /// <param name="copyTo">Copies the file's bytes to a destination stream.</param>
    /// <param name="dispose">Ends the agent stream and gives the transfer slot back.</param>
    public FileDownload(
        string fileName,
        ulong sizeBytes,
        Func<Stream, CancellationToken, Task> copyTo,
        Func<ValueTask> dispose)
    {
        FileName = fileName;
        SizeBytes = sizeBytes;
        _copyTo = copyTo;
        _dispose = dispose;
    }

    /// <summary>Streams the file's bytes to <paramref name="destination"/>.</summary>
    /// <param name="destination">Where the bytes go; the response body.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>A task that ends when the last byte is written.</returns>
    public Task CopyToAsync(Stream destination, CancellationToken cancellationToken)
    {
        return _copyTo(destination, cancellationToken);
    }

    /// <summary>Ends the agent stream and gives the transfer slot back.</summary>
    /// <returns>A task that ends when both are done.</returns>
    public ValueTask DisposeAsync()
    {
        return _dispose();
    }
}
