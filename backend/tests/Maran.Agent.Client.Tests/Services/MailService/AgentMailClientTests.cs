using Maran.Agent.Client.Services.MailService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.MailService;

/// <summary>Mapping contract of AgentMailClient: every request field and every oneof arm.</summary>
public sealed class AgentMailClientTests
{
    /// <summary>A mailbox password hash long enough to be a real one, and unlike any word in a log line.</summary>
    private const string Hash = "$2y$12$Zk3mPq9vXw1LbN8cRt5uOeQh7sDfGjKaYxVnBm2CzTrWe4UiHgLpS";

    /// <summary>A DKIM key body, as the agent might quote it back.</summary>
    private const string Pem = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----\n";

    /// <summary>A wire status that sets every field, each to a value no other field shares.</summary>
    /// <returns>The status.</returns>
    private static MailStatus FullWireStatus()
    {
        var mta = new MailDaemonStatus
        {
            Daemon = MailDaemon.Mta,
            Installed = MailObservation.Yes,
            Version = "3.8.6",
            Active = MailObservation.No,
            ListenersObserved = MailObservation.Yes,
        };
        mta.Listeners.Add(new MailListener { Address = "*", Port = 25 });
        mta.Listeners.Add(new MailListener { Address = "::1", Port = 587 });

        var status = new MailStatus
        {
            Enabled = MailObservation.Yes,
            EffectiveHostname = "mail.example.test",
            TlsInUse = MailObservation.No,
            DovecotMajor = 2,
            DovecotMinor = 4,
            PasswordScheme = MailPasswordScheme.BlfCrypt,
            PasswordSchemeVerified = MailObservation.NotObserved,
            Queue = new MailQueueSize { Messages = 7 },
            SieveAvailable = MailObservation.Yes,
        };
        status.Daemons.Add(mta);

        return status;
    }

    /// <summary>The status every field of which the mapper must carry.</summary>
    /// <param name="status">The mapped status.</param>
    private static void AssertFullStatus(AgentMailStatus status)
    {
        var mta = Assert.Single(status.Daemons);
        Assert.Equal(AgentMailDaemon.Mta, mta.Daemon);
        Assert.Equal(AgentMailObservation.Yes, mta.Installed);
        Assert.Equal("3.8.6", mta.Version);
        Assert.Equal(AgentMailObservation.No, mta.Active);
        Assert.Equal(AgentMailObservation.Yes, mta.ListenersObserved);
        Assert.Equal(
            [new AgentMailListener("*", 25), new AgentMailListener("::1", 587)],
            mta.Listeners);
        Assert.Equal(AgentMailObservation.Yes, status.Enabled);
        Assert.Equal("mail.example.test", status.EffectiveHostname);
        Assert.Equal(AgentMailObservation.No, status.TlsInUse);
        Assert.Equal(2u, status.DovecotMajor);
        Assert.Equal(4u, status.DovecotMinor);
        Assert.Equal(AgentMailPasswordScheme.BlowfishCrypt, status.PasswordScheme);
        Assert.Equal(AgentMailObservation.NotObserved, status.PasswordSchemeVerified);
        Assert.Equal(7u, status.QueuedMessages);
        Assert.Equal(AgentMailObservation.Yes, status.SieveAvailable);
    }

    /// <summary>The status call sends the empty request and maps every observed fact.</summary>
    [Fact]
    public async Task The_status_call_maps_every_observed_fact()
    {
        var stub = new StubMailService
        {
            GetMailStatusResponse = new GetMailStatusResponse { Ok = new GetMailStatusOk { Status = FullWireStatus() } },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.NotNull(stub.LastGetMailStatusRequest);
        Assert.True(result.IsSuccess);
        AssertFullStatus(result.Value);
    }

    /// <summary>Every value the wire leaves at zero reads as unknown, never as a verdict.</summary>
    /// <remarks>
    /// The panel's model must not carry the wire's zero: an unspecified observation is not a "no", a zero
    /// version is not version zero, an absent queue is not an empty one, and an unspecified scheme is not
    /// a scheme.
    /// </remarks>
    [Fact]
    public async Task A_status_the_agent_left_blank_reads_as_unknown_rather_than_as_zeros()
    {
        var stub = new StubMailService
        {
            GetMailStatusResponse = new GetMailStatusResponse { Ok = new GetMailStatusOk { Status = new MailStatus() } },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var status = result.Value;
        Assert.Empty(status.Daemons);
        Assert.Equal(AgentMailObservation.NotObserved, status.Enabled);
        Assert.Equal(AgentMailObservation.NotObserved, status.TlsInUse);
        Assert.Equal(AgentMailObservation.NotObserved, status.PasswordSchemeVerified);
        Assert.Equal(AgentMailObservation.NotObserved, status.SieveAvailable);
        Assert.Null(status.DovecotMajor);
        Assert.Null(status.DovecotMinor);
        Assert.Null(status.PasswordScheme);
        Assert.Null(status.QueuedMessages);
    }

    /// <summary>A queue the agent measured as empty is zero messages, and differs from an absent one.</summary>
    [Fact]
    public async Task A_queue_the_agent_measured_as_empty_is_zero_messages_and_not_absent()
    {
        var status = new MailStatus { Queue = new MailQueueSize { Messages = 0 } };
        var stub = new StubMailService
        {
            GetMailStatusResponse = new GetMailStatusResponse { Ok = new GetMailStatusOk { Status = status } },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.Equal(0u, result.Value.QueuedMessages);
    }

    /// <summary>A daemon entry naming no known daemon makes the whole status unreadable.</summary>
    [Fact]
    public async Task A_status_with_a_daemon_that_names_nothing_is_refused()
    {
        var status = new MailStatus();
        status.Daemons.Add(new MailDaemonStatus { Daemon = MailDaemon.Unspecified });
        var stub = new StubMailService
        {
            GetMailStatusResponse = new GetMailStatusResponse { Ok = new GetMailStatusOk { Status = status } },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>An ok carrying no status message is refused rather than read as an empty stack.</summary>
    [Fact]
    public async Task An_ok_without_a_status_is_refused_rather_than_read_as_an_empty_stack()
    {
        var stub = new StubMailService
        {
            GetMailStatusResponse = new GetMailStatusResponse { Ok = new GetMailStatusOk() },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A failure keeps the agent's code, hides its words from the result and logs them once.</summary>
    [Fact]
    public async Task A_failure_carries_the_agent_code_and_none_of_its_words()
    {
        var logger = new RecordingLogger<AgentMailClient>();
        var stub = new StubMailService
        {
            GetMailStatusResponse = new GetMailStatusResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "postconf: /etc/postfix/main.cf missing" },
            },
        };

        var result = await new AgentMailClient(stub, logger).GetStatusAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.DoesNotContain("/etc/postfix", result.Error.Code, StringComparison.Ordinal);
        Assert.Contains("/etc/postfix/main.cf", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    /// <summary>Every call refuses a response that carries neither branch.</summary>
    [Fact]
    public async Task Every_call_refuses_a_response_with_neither_branch_set()
    {
        var client = Client(new StubMailService());
        var token = CancellationToken.None;
        var settings = Settings(null);
        var domain = new AgentMailDomainArguments("alice", "example.test", "s1", new SensitiveString(Pem));
        var mailbox = new AgentMailboxArguments(
            "alice", "example.test", "info", null, 1024, false, 100, AgentMailSpamAction.FileToJunk);

        var codes = new[]
        {
            (await client.GetStatusAsync(token)).Error?.Code,
            (await client.EnableAsync(settings, token)).Error?.Code,
            (await client.DisableAsync(token)).Error?.Code,
            (await client.ReloadTlsAsync(token)).Error?.Code,
            (await client.ConfigureDomainAsync(domain, token)).Error?.Code,
            (await client.RemoveDomainAsync("alice", "example.test", token)).Error?.Code,
            (await client.SetMailboxAsync(mailbox, token)).Error?.Code,
            (await client.RemoveMailboxAsync("alice", "example.test", "info", false, token)).Error?.Code,
            (await client.SetAliasesAsync("alice", "example.test", [], null, token)).Error?.Code,
            (await client.SetAutoresponderAsync("alice", "example.test", "info", null, token)).Error?.Code,
            (await client.SetAccountSuspensionAsync("alice", true, token)).Error?.Code,
            (await client.RemoveAccountMailAsync("alice", token)).Error?.Code,
            (await client.ListMailboxUsageAsync("alice", token)).Error?.Code,
            (await client.ListQueueAsync(0, token)).Error?.Code,
            (await client.DeleteQueuedMessageAsync("ABCDEF0123456", token)).Error?.Code,
            (await client.FlushQueueAsync(token)).Error?.Code,
            (await client.SetLocalSubmissionAsync(new AgentLocalSubmission("alice", [], 100, [], []), token)).Error?.Code,
            (await client.RemoveLocalSubmissionAsync("alice", token)).Error?.Code,
        };

        Assert.Equal(18, codes.Length);
        Assert.All(codes, code => { Assert.Equal("AgentInvalidResponse", code); });
    }

    /// <summary>Enable sends every setting, and the webmail hash only as the wire string.</summary>
    [Fact]
    public async Task Enable_sends_every_setting_and_maps_the_observation()
    {
        var stub = new StubMailService
        {
            EnableMailResponse = new EnableMailResponse { Ok = new EnableMailOk { Status = FullWireStatus() } },
        };

        var result = await Client(stub).EnableAsync(Settings(new SensitiveString(Hash)), CancellationToken.None);

        var request = Assert.IsType<EnableMailRequest>(stub.LastEnableMailRequest);
        Assert.Equal("mail.example.test", request.Hostname);
        Assert.True(request.Pop3Enabled);
        Assert.True(request.AntivirusEnabled);
        Assert.Equal(50u, request.MessageSizeMib);
        Assert.Equal(3u, request.SpamGreylistScore);
        Assert.Equal(6u, request.SpamAddHeaderScore);
        Assert.Equal(15u, request.SpamRejectScore);
        Assert.True(request.AllowExternalForwarding);
        Assert.Equal(Hash, request.WebmailMasterHash);
        AssertFullStatus(result.Value);
    }

    /// <summary>Enable without a webmail hash sends the empty string, which the wire defines as "no webmail".</summary>
    [Fact]
    public async Task Enable_without_a_webmail_hash_sends_the_empty_string()
    {
        var stub = new StubMailService();

        await Client(stub).EnableAsync(Settings(null), CancellationToken.None);

        Assert.Equal(string.Empty, stub.LastEnableMailRequest!.WebmailMasterHash);
    }

    /// <summary>The settings print their secret redacted, whatever prints them.</summary>
    [Fact]
    public void The_settings_record_never_prints_the_webmail_hash()
    {
        var printed = Settings(new SensitiveString(Hash)).ToString();

        Assert.DoesNotContain(Hash, printed, StringComparison.Ordinal);
        Assert.Contains("mail.example.test", printed, StringComparison.Ordinal);
    }

    /// <summary>A hash the agent quotes back in a refusal is stripped before the log line is written.</summary>
    [Fact]
    public async Task The_webmail_hash_is_never_logged_even_when_the_agent_quotes_it_back()
    {
        var logger = new RecordingLogger<AgentMailClient>();
        var stub = new StubMailService
        {
            EnableMailResponse = new EnableMailResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.InvalidInput,
                    Message = $"master hash '{Hash}' is not a valid bcrypt string",
                },
            },
        };

        var result = await new AgentMailClient(stub, logger)
            .EnableAsync(Settings(new SensitiveString(Hash)), CancellationToken.None);

        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(Hash, logged, StringComparison.Ordinal);
        Assert.Contains("is not a valid bcrypt string", logged, StringComparison.Ordinal);
    }

    /// <summary>Disable and the TLS reload send the empty request and map the observation.</summary>
    [Fact]
    public async Task Disable_and_the_tls_reload_map_the_observation()
    {
        var stub = new StubMailService
        {
            DisableMailResponse = new DisableMailResponse { Ok = new DisableMailOk { Status = FullWireStatus() } },
            ReloadMailTlsResponse = new ReloadMailTlsResponse { Ok = new ReloadMailTlsOk { Status = FullWireStatus() } },
        };
        var client = Client(stub);

        var disabled = await client.DisableAsync(CancellationToken.None);
        var reloaded = await client.ReloadTlsAsync(CancellationToken.None);

        Assert.NotNull(stub.LastDisableMailRequest);
        Assert.NotNull(stub.LastReloadMailTlsRequest);
        AssertFullStatus(disabled.Value);
        AssertFullStatus(reloaded.Value);
    }

    /// <summary>Configuring a domain sends the account, the domain, the selector and the key, and answers "changed".</summary>
    [Fact]
    public async Task Configuring_a_domain_sends_every_field_and_answers_whether_it_changed()
    {
        var stub = new StubMailService
        {
            ConfigureMailDomainResponse = new ConfigureMailDomainResponse { Ok = new ConfigureMailDomainOk { Changed = true } },
        };

        var result = await Client(stub).ConfigureDomainAsync(
            new AgentMailDomainArguments("alice", "example.test", "s2026", new SensitiveString(Pem)),
            CancellationToken.None);

        var request = Assert.IsType<ConfigureMailDomainRequest>(stub.LastConfigureMailDomainRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("example.test", request.Domain);
        Assert.Equal("s2026", request.DkimSelector);
        Assert.Equal(Pem, request.DkimPrivateKeyPem);
        Assert.True(result.Value);
    }

    /// <summary>A key the agent quotes back is stripped from the log, and the surrounding words survive.</summary>
    [Fact]
    public async Task The_dkim_key_is_never_logged_even_when_the_agent_quotes_it_back()
    {
        var logger = new RecordingLogger<AgentMailClient>();
        var stub = new StubMailService
        {
            ConfigureMailDomainResponse = new ConfigureMailDomainResponse
            {
                Error = new AgentError { Code = ErrorCode.AlreadyExists, Message = $"domain taken; key was {Pem}" },
            },
        };

        var result = await new AgentMailClient(stub, logger).ConfigureDomainAsync(
            new AgentMailDomainArguments("alice", "example.test", "s1", new SensitiveString(Pem)),
            CancellationToken.None);

        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain("MIIEvQIBADANBgkqhkiG9w0BAQEFAASC", logged, StringComparison.Ordinal);
        Assert.Contains("domain taken", logged, StringComparison.Ordinal);
    }

    /// <summary>Removing a domain sends the account and the domain.</summary>
    [Fact]
    public async Task Removing_a_domain_sends_the_account_and_the_domain()
    {
        var stub = new StubMailService
        {
            RemoveMailDomainResponse = new RemoveMailDomainResponse { Ok = new RemoveMailDomainOk() },
        };

        var result = await Client(stub).RemoveDomainAsync("alice", "example.test", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", stub.LastRemoveMailDomainRequest!.AccountUsername);
        Assert.Equal("example.test", stub.LastRemoveMailDomainRequest.Domain);
    }

    /// <summary>Setting a mailbox sends every field and maps what the agent changed.</summary>
    [Theory]
    [InlineData(AgentMailSpamAction.FileToJunk, MailSpamAction.FileToJunk)]
    [InlineData(AgentMailSpamAction.KeepInInbox, MailSpamAction.KeepInInbox)]
    public async Task Setting_a_mailbox_sends_every_field_and_maps_what_changed(
        AgentMailSpamAction action,
        MailSpamAction expectedWire)
    {
        var stub = new StubMailService
        {
            SetMailboxResponse = new SetMailboxResponse
            {
                Ok = new SetMailboxOk { Created = true, PasswordChanged = false },
            },
        };

        var result = await Client(stub).SetMailboxAsync(
            new AgentMailboxArguments("alice", "example.test", "info", new SensitiveString(Hash), 2048, true, 80, action),
            CancellationToken.None);

        var request = Assert.IsType<SetMailboxRequest>(stub.LastSetMailboxRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("example.test", request.Domain);
        Assert.Equal("info", request.LocalPart);
        Assert.Equal(Hash, request.PasswordHash);
        Assert.Equal(2048u, request.QuotaMib);
        Assert.True(request.Disabled);
        Assert.Equal(80u, request.SendLimitPerHour);
        Assert.Equal(expectedWire, request.SpamAction);
        Assert.Equal(new AgentMailboxChange(true, false), result.Value);
    }

    /// <summary>A mailbox update without a hash sends the empty string, which keeps the current hash.</summary>
    [Fact]
    public async Task A_mailbox_update_without_a_hash_sends_the_empty_string_that_keeps_the_current_one()
    {
        var stub = new StubMailService();

        await Client(stub).SetMailboxAsync(
            new AgentMailboxArguments("alice", "example.test", "info", null, 1, false, 1, AgentMailSpamAction.FileToJunk),
            CancellationToken.None);

        Assert.Equal(string.Empty, stub.LastSetMailboxRequest!.PasswordHash);
    }

    /// <summary>A mailbox hash the agent quotes back is stripped from the log.</summary>
    [Fact]
    public async Task The_mailbox_hash_is_never_logged_even_when_the_agent_quotes_it_back()
    {
        var logger = new RecordingLogger<AgentMailClient>();
        var stub = new StubMailService
        {
            SetMailboxResponse = new SetMailboxResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = $"rejected hash {Hash} for scheme" },
            },
        };

        var result = await new AgentMailClient(stub, logger).SetMailboxAsync(
            new AgentMailboxArguments("alice", "example.test", "info", new SensitiveString(Hash), 1, false, 1, AgentMailSpamAction.FileToJunk),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(Hash, logged, StringComparison.Ordinal);
        Assert.Contains("rejected hash", logged, StringComparison.Ordinal);
    }

    /// <summary>The mailbox arguments print their hash redacted.</summary>
    [Fact]
    public void The_mailbox_arguments_never_print_the_hash()
    {
        var printed = new AgentMailboxArguments(
            "alice", "example.test", "info", new SensitiveString(Hash), 1, false, 1, AgentMailSpamAction.FileToJunk)
            .ToString();

        Assert.DoesNotContain(Hash, printed, StringComparison.Ordinal);
    }

    /// <summary>Removing a mailbox forwards the data flag as given, in both directions.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Removing_a_mailbox_forwards_the_data_flag_and_answers_whether_data_was_deleted(bool deleteData)
    {
        var stub = new StubMailService
        {
            RemoveMailboxResponse = new RemoveMailboxResponse { Ok = new RemoveMailboxOk { DataDeleted = deleteData } },
        };

        var result = await Client(stub)
            .RemoveMailboxAsync("alice", "example.test", "info", deleteData, CancellationToken.None);

        var request = Assert.IsType<RemoveMailboxRequest>(stub.LastRemoveMailboxRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("example.test", request.Domain);
        Assert.Equal("info", request.LocalPart);
        Assert.Equal(deleteData, request.DeleteData);
        Assert.Equal(deleteData, result.Value);
    }

    /// <summary>Replacing the aliases sends every alias with its targets and the catch-all when there is one.</summary>
    [Fact]
    public async Task Replacing_the_aliases_sends_every_alias_and_the_catch_all()
    {
        var stub = new StubMailService
        {
            SetMailAliasesResponse = new SetMailAliasesResponse
            {
                Ok = new SetMailAliasesOk { AliasCount = 2, CatchAllSet = true },
            },
        };

        var result = await Client(stub).SetAliasesAsync(
            "alice",
            "example.test",
            [
                new AgentMailAlias("sales", ["a@example.test", "b@example.test"]),
                new AgentMailAlias("help", ["c@example.test"]),
            ],
            ["catch@example.test"],
            CancellationToken.None);

        var request = Assert.IsType<SetMailAliasesRequest>(stub.LastSetMailAliasesRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("example.test", request.Domain);
        Assert.Equal(2, request.Aliases.Count);
        Assert.Equal("sales", request.Aliases[0].LocalPart);
        Assert.Equal(["a@example.test", "b@example.test"], request.Aliases[0].Targets);
        Assert.Equal("help", request.Aliases[1].LocalPart);
        Assert.Equal(["c@example.test"], request.Aliases[1].Targets);
        Assert.Equal(["catch@example.test"], request.CatchAll.Targets);
        Assert.Equal(new AgentMailAliasOutcome(2, true), result.Value);
    }

    /// <summary>No catch-all leaves the wire message unset, which is how the contract says "none".</summary>
    [Fact]
    public async Task No_catch_all_leaves_the_wire_message_unset()
    {
        var stub = new StubMailService();

        await Client(stub).SetAliasesAsync("alice", "example.test", [], null, CancellationToken.None);

        Assert.Null(stub.LastSetMailAliasesRequest!.CatchAll);
        Assert.Empty(stub.LastSetMailAliasesRequest.Aliases);
    }

    /// <summary>An autoresponder reaches the wire with its times as unix seconds and its interval.</summary>
    [Fact]
    public async Task An_autoresponder_reaches_the_wire_with_unix_times_and_its_interval()
    {
        var stub = new StubMailService
        {
            SetMailboxAutoresponderResponse = new SetMailboxAutoresponderResponse
            {
                Ok = new SetMailboxAutoresponderOk { Installed = true },
            },
        };
        var starts = new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
        var ends = new DateTimeOffset(2026, 12, 20, 8, 0, 0, TimeSpan.Zero);

        var result = await Client(stub).SetAutoresponderAsync(
            "alice",
            "example.test",
            "info",
            new AgentMailAutoresponder("Away", "Back soon", starts, ends, 3),
            CancellationToken.None);

        var request = Assert.IsType<SetMailboxAutoresponderRequest>(stub.LastSetMailboxAutoresponderRequest);
        Assert.Equal("info", request.LocalPart);
        Assert.Equal("Away", request.Autoresponder.Subject);
        Assert.Equal("Back soon", request.Autoresponder.Body);
        Assert.Equal(starts.ToUnixTimeSeconds(), request.Autoresponder.StartsAtUnix);
        Assert.Equal(ends.ToUnixTimeSeconds(), request.Autoresponder.EndsAtUnix);
        Assert.Equal(3u, request.Autoresponder.ReplyIntervalDays);
        Assert.True(result.Value);
    }

    /// <summary>Absent times and interval are the wire's zeros, and a null reply clears the autoresponder.</summary>
    [Fact]
    public async Task Absent_times_are_zero_and_a_null_reply_leaves_the_message_unset()
    {
        var stub = new StubMailService();
        var client = Client(stub);

        await client.SetAutoresponderAsync(
            "alice", "example.test", "info", new AgentMailAutoresponder("S", "B", null, null, null), CancellationToken.None);
        var open = stub.LastSetMailboxAutoresponderRequest!.Autoresponder;
        await client.SetAutoresponderAsync("alice", "example.test", "info", null, CancellationToken.None);

        Assert.Equal(0, open.StartsAtUnix);
        Assert.Equal(0, open.EndsAtUnix);
        Assert.Equal(0u, open.ReplyIntervalDays);
        Assert.Null(stub.LastSetMailboxAutoresponderRequest.Autoresponder);
    }

    /// <summary>Suspension sends the flag and maps what the agent found and ended.</summary>
    [Fact]
    public async Task Suspension_sends_the_flag_and_maps_what_the_agent_found()
    {
        var stub = new StubMailService
        {
            SetAccountMailSuspensionResponse = new SetAccountMailSuspensionResponse
            {
                Ok = new SetAccountMailSuspensionOk { Mailboxes = 4, SessionsEnded = 2 },
            },
        };

        var result = await Client(stub).SetAccountSuspensionAsync("alice", true, CancellationToken.None);

        Assert.Equal("alice", stub.LastSetAccountMailSuspensionRequest!.AccountUsername);
        Assert.True(stub.LastSetAccountMailSuspensionRequest.Suspended);
        Assert.Equal(new AgentMailSuspensionOutcome(4, 2), result.Value);
    }

    /// <summary>Removing an account's mail maps both counts.</summary>
    [Fact]
    public async Task Removing_an_accounts_mail_maps_both_counts()
    {
        var stub = new StubMailService
        {
            RemoveAccountMailResponse = new RemoveAccountMailResponse
            {
                Ok = new RemoveAccountMailOk { MailboxesRemoved = 5, DomainsRemoved = 2 },
            },
        };

        var result = await Client(stub).RemoveAccountMailAsync("alice", CancellationToken.None);

        Assert.Equal("alice", stub.LastRemoveAccountMailRequest!.AccountUsername);
        Assert.Equal(new AgentAccountMailRemoval(5, 2), result.Value);
    }

    /// <summary>A mailbox the agent could not measure has no reading, and one it measured as empty has zeros.</summary>
    [Fact]
    public async Task Usage_keeps_an_unmeasured_mailbox_apart_from_an_empty_one()
    {
        var ok = new ListMailboxUsageOk();
        ok.Mailboxes.Add(new MailboxUsage
        {
            Domain = "example.test",
            LocalPart = "full",
            Reading = new MailboxUsageReading { BytesUsed = 4096, MessageCount = 12 },
        });
        ok.Mailboxes.Add(new MailboxUsage
        {
            Domain = "example.test",
            LocalPart = "empty",
            Reading = new MailboxUsageReading { BytesUsed = 0, MessageCount = 0 },
        });
        ok.Mailboxes.Add(new MailboxUsage { Domain = "example.test", LocalPart = "unmeasured" });
        var stub = new StubMailService { ListMailboxUsageResponse = new ListMailboxUsageResponse { Ok = ok } };

        var result = await Client(stub).ListMailboxUsageAsync("alice", CancellationToken.None);

        Assert.Equal("alice", stub.LastListMailboxUsageRequest!.AccountUsername);
        Assert.Equal(
            [
                new AgentMailboxUsage("example.test", "full", new AgentMailboxUsageReading(4096, 12)),
                new AgentMailboxUsage("example.test", "empty", new AgentMailboxUsageReading(0, 0)),
                new AgentMailboxUsage("example.test", "unmeasured", null),
            ],
            result.Value);
    }

    /// <summary>The queue listing sends the limit and maps every entry field.</summary>
    [Fact]
    public async Task The_queue_listing_sends_the_limit_and_maps_every_entry_field()
    {
        var ok = new ListMailQueueOk { TotalMessages = 40, Truncated = true };
        ok.Entries.Add(new MailQueueEntry
        {
            QueueId = "4F2A9C01B7D",
            Queue = MailQueueName.Deferred,
            Sender = "someone@remote.test",
            RecipientCount = 3,
            SizeBytes = 123456,
            ArrivedAtUnix = 1_790_000_000,
            DeferralClass = MailDeferralClass.RemoteRefused,
        });
        var stub = new StubMailService { ListMailQueueResponse = new ListMailQueueResponse { Ok = ok } };

        var result = await Client(stub).ListQueueAsync(25, CancellationToken.None);

        Assert.Equal(25u, stub.LastListMailQueueRequest!.Limit);
        var queue = result.Value;
        Assert.Equal(40u, queue.TotalMessages);
        Assert.True(queue.Truncated);
        Assert.Equal(
            new AgentMailQueueEntry(
                "4F2A9C01B7D",
                AgentMailQueueName.Deferred,
                "someone@remote.test",
                3,
                123456,
                DateTimeOffset.FromUnixTimeSeconds(1_790_000_000),
                AgentMailDeferralClass.RemoteRefused),
            Assert.Single(queue.Entries));
    }

    /// <summary>An entry naming no queue or no deferral class makes the listing unreadable.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_queue_entry_that_names_no_queue_or_no_class_is_refused(bool queueMissing)
    {
        var ok = new ListMailQueueOk();
        ok.Entries.Add(new MailQueueEntry
        {
            QueueId = "4F2A9C01B7D",
            Queue = queueMissing ? MailQueueName.Unspecified : MailQueueName.Active,
            DeferralClass = queueMissing ? MailDeferralClass.None : MailDeferralClass.Unspecified,
        });
        var stub = new StubMailService { ListMailQueueResponse = new ListMailQueueResponse { Ok = ok } };

        var result = await Client(stub).ListQueueAsync(0, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Deleting a queued message sends its id; a missing message is the agent's NotFound.</summary>
    [Fact]
    public async Task Deleting_a_queued_message_sends_its_id_and_passes_a_not_found_through()
    {
        var stub = new StubMailService
        {
            DeleteQueuedMessageResponse = new DeleteQueuedMessageResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such queue id" },
            },
        };

        var result = await Client(stub).DeleteQueuedMessageAsync("4F2A9C01B7D", CancellationToken.None);

        Assert.Equal("4F2A9C01B7D", stub.LastDeleteQueuedMessageRequest!.QueueId);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A local submission binding reaches the wire whole: limit and both lists.</summary>
    [Fact]
    public async Task Setting_a_local_submission_sends_the_whole_binding()
    {
        var stub = new StubMailService
        {
            SetLocalSubmissionResponse = new SetLocalSubmissionResponse { Ok = new SetLocalSubmissionOk() },
        };

        var result = await Client(stub).SetLocalSubmissionAsync(
            new AgentLocalSubmission(
                "alice",
                ["a.test", "b.test"],
                250,
                ["info@a.test"],
                [new AgentAliasFanout("sales@a.test", 3)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = stub.LastSetLocalSubmissionRequest!;
        Assert.Equal("alice", sent.AccountUsername);
        Assert.Equal(250u, sent.SendLimitPerHour);
        Assert.Equal(["a.test", "b.test"], sent.Domains);
        Assert.Equal(["info@a.test"], sent.MailboxLogins);
        var fanout = Assert.Single(sent.AliasFanouts);
        Assert.Equal("sales@a.test", fanout.Alias);
        Assert.Equal(3u, fanout.ExternalTargets);
    }

    /// <summary>The inverse control: a binding without forwarding aliases sends none.</summary>
    [Fact]
    public async Task Setting_a_local_submission_without_forwarders_sends_no_fanouts()
    {
        var stub = new StubMailService
        {
            SetLocalSubmissionResponse = new SetLocalSubmissionResponse { Ok = new SetLocalSubmissionOk() },
        };

        await Client(stub).SetLocalSubmissionAsync(
            new AgentLocalSubmission("alice", ["a.test"], 250, [], []),
            CancellationToken.None);

        Assert.Empty(stub.LastSetLocalSubmissionRequest!.AliasFanouts);
    }

    /// <summary>The inverse control: the agent's refusal comes back typed, and an empty answer is invalid.</summary>
    [Fact]
    public async Task Setting_a_local_submission_passes_a_refusal_through_and_rejects_an_empty_answer()
    {
        var refused = new StubMailService
        {
            SetLocalSubmissionResponse = new SetLocalSubmissionResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "uid out of range" },
            },
        };
        var empty = new StubMailService();
        var submission = new AgentLocalSubmission("alice", [], 250, [], []);

        var refusal = await Client(refused).SetLocalSubmissionAsync(submission, CancellationToken.None);
        var invalid = await Client(empty).SetLocalSubmissionAsync(submission, CancellationToken.None);

        Assert.False(refusal.IsSuccess);
        Assert.NotEqual("AgentInvalidResponse", refusal.Error!.Code);
        Assert.Equal("AgentInvalidResponse", invalid.Error!.Code);
    }

    /// <summary>Removing a binding sends the account's username; the agent's NotFound passes through.</summary>
    [Fact]
    public async Task Removing_a_local_submission_sends_the_username_and_maps_both_branches()
    {
        var ok = new StubMailService
        {
            RemoveLocalSubmissionResponse = new RemoveLocalSubmissionResponse { Ok = new RemoveLocalSubmissionOk() },
        };
        var missing = new StubMailService
        {
            RemoveLocalSubmissionResponse = new RemoveLocalSubmissionResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such account" },
            },
        };

        var removed = await Client(ok).RemoveLocalSubmissionAsync("alice", CancellationToken.None);
        var absent = await Client(missing).RemoveLocalSubmissionAsync("alice", CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.Equal("alice", ok.LastRemoveLocalSubmissionRequest!.AccountUsername);
        Assert.Equal("AgentNotFound", absent.Error!.Code);
    }

    /// <summary>A flush reports the queue size afterwards, and null when the agent did not read it.</summary>
    [Fact]
    public async Task A_flush_reports_the_queue_size_and_null_when_it_was_not_read()
    {
        var read = new StubMailService
        {
            FlushMailQueueResponse = new FlushMailQueueResponse
            {
                Ok = new FlushMailQueueOk { Queue = new MailQueueSize { Messages = 0 } },
            },
        };
        var unread = new StubMailService
        {
            FlushMailQueueResponse = new FlushMailQueueResponse { Ok = new FlushMailQueueOk() },
        };

        var measured = await Client(read).FlushQueueAsync(CancellationToken.None);
        var absent = await Client(unread).FlushQueueAsync(CancellationToken.None);

        Assert.Equal(0u, measured.Value);
        Assert.Null(absent.Value);
    }

    /// <summary>Builds the settings every test shares, with the given webmail hash.</summary>
    /// <param name="webmailHash">The hash, or null for no webmail.</param>
    /// <returns>The settings.</returns>
    private static AgentMailServerSettings Settings(SensitiveString? webmailHash)
    {
        return new AgentMailServerSettings("mail.example.test", true, true, 50, 3, 6, 15, true, webmailHash);
    }

    /// <summary>Builds a client over a stub with a logger that discards everything.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentMailClient Client(StubMailService stub)
    {
        return new AgentMailClient(stub, NullLogger<AgentMailClient>.Instance);
    }
}
