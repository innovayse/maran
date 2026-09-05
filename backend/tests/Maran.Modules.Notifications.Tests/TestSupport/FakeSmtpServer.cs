using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>
/// A mail server on loopback that answers just enough SMTP to reveal which socket option the mailer
/// connected with.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the choice this test suite has to hold in place — the mapping from
/// <c>SmtpSecurity</c> onto MailKit's <c>SecureSocketOptions</c> — is invisible from outside the
/// process unless something is listening. A double for <c>SmtpClient</c> would prove nothing: the
/// mapping is only real once a socket carries it.
/// </para>
/// <para>
/// <b>It deliberately never advertises STARTTLS and never speaks TLS.</b> That is what makes the
/// three modes tell themselves apart on the wire: a plain connection completes a whole transaction,
/// a STARTTLS connection sends <c>EHLO</c> in the clear and then gives up because the extension it
/// requires is missing, and an implicit-TLS connection opens with a TLS record before a word of SMTP
/// has been spoken. A server that offered STARTTLS would make the first two indistinguishable, which
/// is precisely the confusion a mutated discard arm hides behind.
/// </para>
/// </remarks>
public sealed class FakeSmtpServer : IDisposable
{
    /// <summary>The first byte of a TLS handshake record, which is how an implicit-TLS client opens.</summary>
    private const byte TlsHandshakeRecord = 0x16;

    /// <summary>The socket accepting connections on loopback.</summary>
    private readonly TcpListener _listener;

    /// <summary>Stops the accept loop when the test disposes the server.</summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>The SMTP verbs received, in order; guarded by its own lock.</summary>
    private readonly List<string> _commands = [];

    /// <summary>Whether this server hangs up the moment it has greeted, instead of conversing.</summary>
    private readonly bool _hangsUpAfterGreeting;

    /// <summary>The accept loop, kept so disposal can wait for it to unwind.</summary>
    private readonly Task _serving;

    /// <summary>Whether a client opened with a TLS record rather than waiting for the greeting.</summary>
    private volatile bool _sawTlsHandshake;

    /// <summary>How many messages were accepted end to end, through <c>DATA</c> and its terminator.</summary>
    private int _acceptedMessages;

    /// <summary>Starts a server on a free loopback port.</summary>
    /// <param name="hangsUpAfterGreeting">
    /// When set, the connection is closed immediately after the greeting — a mail server behaving
    /// badly, which is the case the mailer promises to turn into a result rather than an exception.
    /// </param>
    public FakeSmtpServer(bool hangsUpAfterGreeting = false)
    {
        _hangsUpAfterGreeting = hangsUpAfterGreeting;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serving = Task.Run(ServeAsync);
    }

    /// <summary>The loopback port this server is listening on.</summary>
    public int Port { get; }

    /// <summary>Whether a client began with a TLS handshake instead of reading the greeting.</summary>
    public bool SawTlsHandshake
    {
        get
        {
            return _sawTlsHandshake;
        }
    }

    /// <summary>How many messages completed the whole transaction.</summary>
    public int AcceptedMessages
    {
        get
        {
            return Volatile.Read(ref _acceptedMessages);
        }
    }

    /// <summary>The SMTP verbs this server was sent, upper-cased, in order.</summary>
    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return [.. _commands];
            }
        }
    }

    /// <summary>Stops the listener and waits for the accept loop to unwind.</summary>
    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();

        try
        {
            _serving.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // A socket torn down mid-read faults the loop; that is what disposal asked for.
        }

        _stopping.Dispose();
    }

    /// <summary>Finds the end of the first complete line in a buffer.</summary>
    /// <param name="buffered">The bytes read so far.</param>
    /// <returns>The index of the line feed, or -1 when no line is complete yet.</returns>
    private static int IndexOfLineFeed(List<byte> buffered)
    {
        return buffered.IndexOf((byte)'\n');
    }

    /// <summary>Writes one SMTP response line.</summary>
    /// <param name="stream">The connection to write to.</param>
    /// <param name="line">The line, without its terminator.</param>
    /// <returns>Resolves once the line has been flushed.</returns>
    private static Task WriteLineAsync(NetworkStream stream, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        return stream.WriteAsync(bytes).AsTask();
    }

    /// <summary>Accepts connections until the server is disposed.</summary>
    /// <returns>Resolves when the listener stops.</returns>
    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception)
            {
                // The listener was stopped, which is the only way out of this loop.
                return;
            }

            using (client)
            {
                try
                {
                    await ConverseAsync(client);
                }
                catch (Exception)
                {
                    // A client that vanishes mid-conversation is one of the outcomes under test.
                }
            }
        }
    }

    /// <summary>Greets one client and answers its commands.</summary>
    /// <param name="client">The accepted connection.</param>
    /// <returns>Resolves when the conversation ends.</returns>
    private async Task ConverseAsync(TcpClient client)
    {
        var stream = client.GetStream();
        await WriteLineAsync(stream, "220 fake ESMTP ready");

        if (_hangsUpAfterGreeting)
        {
            client.Close();
            return;
        }

        var buffered = new List<byte>();

        while (true)
        {
            var line = await ReadLineAsync(stream, buffered);

            if (line is null)
            {
                return;
            }

            var verb = line.Split(' ')[0].ToUpperInvariant();

            lock (_commands)
            {
                _commands.Add(verb);
            }

            if (verb is "EHLO" or "HELO")
            {
                // No STARTTLS and no AUTH: see the type's remarks, the omission is the discriminator.
                await WriteLineAsync(stream, "250-fake greets you");
                await WriteLineAsync(stream, "250-SIZE 10485760");
                await WriteLineAsync(stream, "250 HELP");
                continue;
            }

            if (verb is "MAIL" or "RCPT" or "RSET" or "NOOP")
            {
                await WriteLineAsync(stream, "250 2.1.0 Ok");
                continue;
            }

            if (verb == "DATA")
            {
                await WriteLineAsync(stream, "354 End data with <CR><LF>.<CR><LF>");
                await ReadUntilTerminatorAsync(stream, buffered);
                Interlocked.Increment(ref _acceptedMessages);
                await WriteLineAsync(stream, "250 2.0.0 Ok: queued");
                continue;
            }

            if (verb == "QUIT")
            {
                await WriteLineAsync(stream, "221 2.0.0 Bye");
                return;
            }

            await WriteLineAsync(stream, "502 5.5.2 Command not implemented");
        }
    }

    /// <summary>Swallows a message body up to the lone-dot terminator.</summary>
    /// <param name="stream">The connection to read from.</param>
    /// <param name="buffered">The read buffer shared with the command loop.</param>
    /// <returns>Resolves at the terminator, or when the client disconnects.</returns>
    private async Task ReadUntilTerminatorAsync(NetworkStream stream, List<byte> buffered)
    {
        while (true)
        {
            var line = await ReadLineAsync(stream, buffered);

            if (line is null || line == ".")
            {
                return;
            }
        }
    }

    /// <summary>Reads one CRLF-terminated line, noting a TLS handshake if that is what arrives.</summary>
    /// <param name="stream">The connection to read from.</param>
    /// <param name="buffered">Bytes carried over from the previous read.</param>
    /// <returns>The line without its terminator, or <c>null</c> at end of stream or on a TLS opening.</returns>
    private async Task<string?> ReadLineAsync(NetworkStream stream, List<byte> buffered)
    {
        var chunk = new byte[1024];

        while (true)
        {
            var end = IndexOfLineFeed(buffered);

            if (end >= 0)
            {
                var line = Encoding.ASCII.GetString([.. buffered.Take(end)]).TrimEnd('\r');
                buffered.RemoveRange(0, end + 1);
                return line;
            }

            var read = await stream.ReadAsync(chunk, _stopping.Token);

            if (read == 0)
            {
                return null;
            }

            if (buffered.Count == 0 && chunk[0] == TlsHandshakeRecord)
            {
                // A client that speaks TLS before reading the greeting asked for implicit TLS. There
                // is nothing to answer it with, so the connection ends here.
                _sawTlsHandshake = true;
                return null;
            }

            buffered.AddRange(chunk.Take(read));
        }
    }
}
