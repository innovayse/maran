using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Identity.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="User"/> onto the <c>identity.Users</c> table.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Maximum stored length of the encrypted TOTP secret, which is longer than the plaintext.</summary>
    private const int EncryptedTotpSecretLength = 256;

    /// <summary>The cipher applied to <see cref="User.TotpSecret"/>.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration with the cipher its secret column needs.</summary>
    /// <param name="encryptionService">The cipher applied to the TOTP secret column.</param>
    public UserConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, keys, and column constraints for <see cref="User"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        // The one lifecycle column the code reads. Required with a database default so the additive
        // migration could add it to a table that already has rows; Active is what the previous
        // release's rows overwhelmingly are. Legacy columns that stay mapped and are written by
        // User.SyncLegacyColumns until the contract release: "State" (LegacyState) and
        // "IsAccountSuspended" (LegacyAccountSuspended). They restate the suspension of the account
        // the login owns, which the previous release reads to refuse a sign-in; this release's truth
        // for it is the owner membership's state. The "IsTotpEnabled" column (LegacyTotpEnabled) is likewise kept and
        // written by EnableTotp/DisableTotp; the code reads TotpSecret instead, which already holds
        // the same fact.
        builder.Property(u => u.Lifecycle)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(UserLifecycle.Active);

        builder.Property(u => u.LegacyState)
            .HasColumnName("State")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(UserState.Active);

        builder.Property(u => u.LegacyAccountSuspended)
            .HasColumnName("IsAccountSuspended")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.LegacyTotpEnabled)
            .HasColumnName("IsTotpEnabled")
            .IsRequired();

        // The reseller a login is the one login of. Nullable and never a foreign key: the reseller row
        // lives in the Accounts schema, which this module does not read. Additive: a login that exists
        // before the migration reads back null, which is "not a reseller's". The role itself needs no
        // column change: Role is stored as its name in a character varying(16) with no CHECK
        // constraint, and "Reseller" is eight characters.
        builder.Property(u => u.ResellerId);

        // Whether a reseller's login is suspended (User.Suspend). Required with a database default so
        // the additive migration could add it to a table that already has rows: every existing login
        // is not suspended on the login itself, because a customer's suspension is on its membership.
        builder.Property(u => u.IsSuspended)
            .IsRequired()
            .HasDefaultValue(false);

        // Whether the login's first password was chosen by somebody else and must be replaced (see
        // User.PasswordChangeRequired). EXPAND-ONLY: required with a database default so the additive
        // migration could add it to a table that already has rows (every existing login chose its own
        // password), and so the previous release, which does not know the column, still inserts logins.
        builder.Property(u => u.PasswordChangeRequired)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        // The TOTP secret is a second factor sitting at rest: anyone who reads this column can
        // generate the codes it protects. EncryptedStringConverter keeps the ciphertext in
        // PostgreSQL while the domain still reads plain text (rules/security.md item 8).
        // The `!` states what EF Core guarantees and the type system cannot: a value converter is
        // never handed a null. EncryptedStringConverter is declared over non-null strings — the
        // shape every other column wants — while this property is nullable because "no second
        // factor" is a real state, so the two disagree on paper and agree in practice.
        builder.Property(u => u.TotpSecret)
            .HasConversion(new EncryptedStringConverter(_encryptionService)!)
            .HasMaxLength(EncryptedTotpSecretLength);

        // Both are how a user is looked up at login, and both must identify exactly one person:
        // enforced by the database rather than only by the command, so a race cannot create twins.
        builder.HasIndex(u => u.Username).IsUnique().HasDatabaseName("IX_Users_Username");
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");

        // One login per reseller, enforced by the database so two deliveries of ResellerCreated cannot
        // create twins. Partial, because PostgreSQL counts NULLs as distinct in a unique index and
        // every login that is not a reseller's holds NULL; the filter also keeps it PostgreSQL 13
        // compatible (NULLS NOT DISTINCT is 15 and later).
        builder.HasIndex(u => u.ResellerId)
            .IsUnique()
            .HasFilter("\"ResellerId\" IS NOT NULL")
            .HasDatabaseName("UX_Users_ResellerId");

        // EXPAND-ONLY: the previous release (1.0.0-beta.4) reads and writes Users."AccountId" and its
        // partial unique index, so both stay in the schema for one more release. This release neither
        // reads nor writes the column: it is a SHADOW property, so no entity member exposes it and a
        // new login stores NULL there (the column has always been nullable, an administrator's is
        // NULL). The account a login may act for is its AccountMembership rows. It is named
        // LegacyAccountId, not AccountId, so the tenant census (TenantScopeTests) does not mistake a
        // login for an account's row. The release after beta.5 drops the column and the index.
        // Partial, because PostgreSQL counts NULLs as distinct in a unique index.
        builder.Property<Guid?>("LegacyAccountId").HasColumnName("AccountId");
        builder.HasIndex("LegacyAccountId")
            .IsUnique()
            .HasFilter("\"AccountId\" IS NOT NULL")
            .HasDatabaseName("UX_Users_AccountId");
    }
}
