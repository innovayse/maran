using System.Text;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// The files under every account's home on the simulated server, bounded in entries and in bytes and
/// safe to share between requests.
/// </summary>
/// <remarks>
/// The content is whatever a visitor typed, so it is held as data and nothing here reads it as
/// anything else: it is never executed, never written to a disk and never served back with a content
/// type of its own. Paths are relative to the account's home, are contained by construction — an
/// absolute path, a <c>..</c> segment or a control character is refused — and the account is part of
/// the key, so one account's path cannot name another's file.
/// </remarks>
public sealed class SimulatedFileStore
{
    /// <summary>The permission bits a directory created on the way to a file carries.</summary>
    private const uint DirectoryMode = 0b111_101_101;

    /// <summary>The twelve permission bits a mode can carry: set-id and sticky, then owner, group, other.</summary>
    private const uint ModeMask = 0xFFF;

    /// <summary>The separator of a relative path.</summary>
    private const char Separator = '/';

    /// <summary>Serialises every read and write of the entries and the byte counts.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The entries, keyed by owning account and normalised relative path.</summary>
    private readonly Dictionary<(string Account, string Path), SimulatedFile> _entries = [];

    /// <summary>The bytes of content each account holds.</summary>
    private readonly Dictionary<string, long> _accountBytes = [];

    /// <summary>The bytes of content the whole server holds.</summary>
    private long _totalBytes;

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedFileStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>Writes one file, creating the directories on the way to it.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="path">Destination path relative to the account's home.</param>
    /// <param name="content">The file's entire content.</param>
    /// <param name="mode">Permission bits; only the twelve a mode can carry are kept.</param>
    /// <returns>
    /// The bytes written, <c>AgentInvalidInput</c> for a path that escapes the home, names the home
    /// itself, runs through a file or names a directory, <c>AgentBelowFreeSpaceFloor</c> when the
    /// account or the server holds as many bytes as it will, or <c>AgentLimitReached</c> when it holds
    /// as many entries.
    /// </returns>
    public Result<ulong> Write(string account, string path, string content, uint mode)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength) || !TryNormalise(path, out var normalised))
        {
            return Result<ulong>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        var bytes = Encoding.UTF8.GetByteCount(content);

        lock (_gate)
        {
            if (_entries.TryGetValue((account, normalised), out var existing) && existing.IsDirectory)
            {
                return Result<ulong>.Fail(SimulatedAgentErrors.InvalidInput());
            }

            var missingDirectories = MissingDirectories(account, normalised);
            if (missingDirectories is null)
            {
                return Result<ulong>.Fail(SimulatedAgentErrors.InvalidInput());
            }

            var replacedBytes = existing is null ? 0 : Encoding.UTF8.GetByteCount(existing.Content);
            var added = missingDirectories.Count + (existing is null ? 1 : 0);
            var accountBytes = _accountBytes.GetValueOrDefault(account) - replacedBytes + bytes;
            var totalBytes = _totalBytes - replacedBytes + bytes;

            if (accountBytes > _limits.MaxFileBytesPerAccount
                || totalBytes > _limits.MaxFileBytesTotal)
            {
                return Result<ulong>.Fail(SimulatedAgentErrors.BelowFreeSpaceFloor());
            }

            if (_entries.Count + added > _limits.MaxFileEntries)
            {
                return Result<ulong>.Fail(SimulatedAgentErrors.LimitReached());
            }

            foreach (var directory in missingDirectories)
            {
                _entries[(account, directory)] = new SimulatedFile(true, string.Empty, DirectoryMode);
            }

            _entries[(account, normalised)] = new SimulatedFile(false, content, mode & ModeMask);
            _accountBytes[account] = accountBytes;
            _totalBytes = totalBytes;

            return Result<ulong>.Ok((ulong)bytes);
        }
    }

    /// <summary>Deletes one file or directory.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="path">Path to delete, relative to the account's home.</param>
    /// <param name="recursive">Whether a directory that still holds entries may be removed with them.</param>
    /// <returns>
    /// Success, <c>AgentNotFound</c> for a path that is already gone, or <c>AgentInvalidInput</c> for
    /// a path that escapes the home or a non-empty directory deleted without <paramref name="recursive"/>.
    /// </returns>
    public Result Delete(string account, string path, bool recursive)
    {
        if (!TryNormalise(path, out var normalised))
        {
            return Result.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue((account, normalised), out var entry))
            {
                return Result.Fail(SimulatedAgentErrors.NotFound());
            }

            var prefix = normalised + Separator;
            var descendants = _entries.Keys
                .Where(key =>
                {
                    return key.Account == account && key.Path.StartsWith(prefix, StringComparison.Ordinal);
                })
                .ToList();

            if (entry.IsDirectory && descendants.Count > 0 && !recursive)
            {
                return Result.Fail(SimulatedAgentErrors.InvalidInput());
            }

            foreach (var descendant in descendants)
            {
                Remove(descendant);
            }

            Remove((account, normalised));

            return Result.Ok();
        }
    }

    /// <summary>Reads one entry.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="path">Path relative to the account's home.</param>
    /// <returns>The entry, or <c>null</c> when the path is unusable or holds nothing.</returns>
    public SimulatedFile? Find(string account, string path)
    {
        if (!TryNormalise(path, out var normalised))
        {
            return null;
        }

        lock (_gate)
        {
            return _entries.GetValueOrDefault((account, normalised));
        }
    }

    /// <summary>Counts the bytes of content one account holds.</summary>
    /// <param name="account">System username of the account.</param>
    /// <returns>The bytes held; zero for an account with no file.</returns>
    public long BytesHeldBy(string account)
    {
        lock (_gate)
        {
            return _accountBytes.GetValueOrDefault(account);
        }
    }

    /// <summary>Turns a caller's path into the form entries are keyed by, refusing any that escapes the home.</summary>
    /// <param name="path">The path as the caller sent it.</param>
    /// <param name="normalised">The path without empty, <c>.</c> or leading separators, on success.</param>
    /// <returns>
    /// <c>false</c> for a blank, over-long, absolute, control-character or <c>..</c> path, or one naming the home
    /// itself.
    /// </returns>
    private bool TryNormalise(string? path, out string normalised)
    {
        normalised = string.Empty;
        if (string.IsNullOrEmpty(path)
            || path.Length > _limits.MaxFilePathLength
            || path[0] == Separator
            || path.Any(char.IsControl))
        {
            return false;
        }

        var segments = path.Split(Separator).Where(segment =>
        {
            return segment.Length > 0 && segment != ".";
        }).ToList();
        if (segments.Count == 0 || segments.Contains(".."))
        {
            return false;
        }

        normalised = string.Join(Separator, segments);

        return true;
    }

    /// <summary>
    /// Lists the directories a file's path needs that do not exist yet; the caller holds <see cref="_gate"/>.
    /// </summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="normalised">The file's normalised path.</param>
    /// <returns>The missing directories, outermost first, or <c>null</c> when a parent is a file.</returns>
    private List<string>? MissingDirectories(string account, string normalised)
    {
        var missing = new List<string>();
        var segments = normalised.Split(Separator);
        var current = string.Empty;

        foreach (var segment in segments.Take(segments.Length - 1))
        {
            current = current.Length == 0 ? segment : current + Separator + segment;
            if (!_entries.TryGetValue((account, current), out var parent))
            {
                missing.Add(current);
            }
            else if (!parent.IsDirectory)
            {
                return null;
            }
        }

        return missing;
    }

    /// <summary>Removes one entry and the bytes it held; the caller holds <see cref="_gate"/>.</summary>
    /// <param name="key">The entry's account and normalised path.</param>
    private void Remove((string Account, string Path) key)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return;
        }

        var bytes = Encoding.UTF8.GetByteCount(entry.Content);
        _accountBytes[key.Account] = _accountBytes.GetValueOrDefault(key.Account) - bytes;
        _totalBytes -= bytes;
    }
}
