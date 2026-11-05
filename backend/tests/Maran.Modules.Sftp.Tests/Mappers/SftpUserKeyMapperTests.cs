using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Tests.TestSupport;

namespace Maran.Modules.Sftp.Tests.Mappers;

/// <summary>How a key row is restated for a screen.</summary>
public sealed class SftpUserKeyMapperTests
{
    /// <summary>The wire shape carries the row's identity, label, algorithm, fingerprint and instant, and nothing else.</summary>
    [Fact]
    public void The_wire_shape_carries_the_rows_identity_label_algorithm_fingerprint_and_instant()
    {
        var login = SftpTestContext.Row(Guid.NewGuid(), "alice", "deploy");
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var key = login.AddKey(id, "laptop", SftpPublicKey.Parse(SftpTestKeys.P256).Value, at).Value;

        var dto = SftpUserKeyMapper.From(key);

        Assert.Equal(id, dto.Id);
        Assert.Equal("laptop", dto.Label);
        Assert.Equal("ecdsa-sha2-nistp256", dto.Algorithm);
        Assert.Equal(SftpTestKeys.P256Fingerprint, dto.Fingerprint);
        Assert.Equal(at, dto.CreatedAt);
        Assert.DoesNotContain(SftpTestKeys.P256.Split(' ')[1], dto.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A missing row is refused.</summary>
    [Fact]
    public void A_missing_row_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            return SftpUserKeyMapper.From(null!);
        });
    }
}
