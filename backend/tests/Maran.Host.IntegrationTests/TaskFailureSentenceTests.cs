using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A failed task carries its failure as a sentence the backend wrote, in the reader's language, with
/// the code beside it — on the single read, on the list and on every frame of the stream (issue #272).
/// </summary>
/// <remarks>
/// <para>
/// The failure used is another module's code, <c>AcmeAccountRejected</c> from Ssl, stored on a row
/// of the Tasks module — exactly the case the SPA's own table of task sentences could not see,
/// because the code is built in a file far from the task. What makes it resolve here is that the
/// Tasks module reads it through the panel's composed error-text pool, the same one every problem
/// response is written from, so this test observes the composed host and not a unit's stub.
/// </para>
/// <para>
/// The sentence is resolved when the row is READ: the row is seeded once and read three times, each
/// in a different language, and each answer is that language's own entry. A sentence frozen at write
/// time would answer all three in one language.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TaskFailureSentenceTests : IAsyncLifetime
{
    /// <summary>The failure code the seeded task carries: Ssl's, not the Tasks module's own.</summary>
    private const string FailureCode = "AcmeAccountRejected";

    /// <summary>The bound on every stream read, so a missing frame fails rather than hangs.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TaskFailureSentenceTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Each shipped language with the sentence Ssl's own table writes for the failure.</summary>
    /// <returns>Language and sentence pairs.</returns>
    public static TheoryData<string, string> SentencePerLanguage()
    {
        return new TheoryData<string, string>
        {
            { "en", "The certificate authority refused this server's registration." },
            { "ru", "Центр сертификации отклонил регистрацию этого сервера." },
            { "hy", "Վկայագրման կենտրոնը մերժեց այս սերվերի գրանցումը։" },
        };
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// A failed task read in a language carries that language's sentence and the code, on the single
    /// read, on the list and on the stream's frame alike.
    /// </summary>
    /// <param name="language">The reader's <c>Accept-Language</c>.</param>
    /// <param name="sentence">The sentence that language's table holds for the failure.</param>
    [Theory]
    [MemberData(nameof(SentencePerLanguage))]
    public async Task A_failed_task_carries_its_sentence_in_the_readers_language_on_every_door(
        string language,
        string sentence)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var taskId = await SeedFailedTaskAsync(factory, FailureCode);
        using var admin = await SignInSpeakingAsync(factory, language);

        using var single = await GetJsonAsync(admin, $"/api/v1/tasks/{taskId}");
        using var page = await GetJsonAsync(admin, "/api/v1/tasks?status=failed");
        var frame = await FirstStreamFrameAsync(admin, taskId);

        AssertFailure(single.RootElement, sentence);
        AssertFailure(page.RootElement.GetProperty("items")[0], sentence);
        AssertFailure(frame, sentence);
    }

    /// <summary>
    /// Two watchers of one task, in two languages, are each sent frames in their own language: the
    /// stream resolves per subscriber, not once per task.
    /// </summary>
    [Fact]
    public async Task Two_watchers_of_one_task_each_read_its_frames_in_their_own_language()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var taskId = await SeedFailedTaskAsync(factory, FailureCode);
        using var russian = await SignInSpeakingAsync(factory, "ru");
        using var armenian = await SignInSpeakingAsync(factory, "hy");

        var russianFrame = await FirstStreamFrameAsync(russian, taskId);
        var armenianFrame = await FirstStreamFrameAsync(armenian, taskId);

        AssertFailure(russianFrame, "Центр сертификации отклонил регистрацию этого сервера.");
        AssertFailure(armenianFrame, "Վկայագրման կենտրոնը մերժեց այս սերվերի գրանցումը։");
    }

    /// <summary>
    /// A code no loaded module describes reads as the Tasks module's generic sentence, with the code
    /// beside it — never as the code where a sentence belongs.
    /// </summary>
    [Fact]
    public async Task A_code_no_module_describes_reads_as_the_generic_sentence_with_the_code_beside_it()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var taskId = await SeedFailedTaskAsync(factory, "RemovedModuleFailure");
        using var admin = await SignInSpeakingAsync(factory, "en");

        using var single = await GetJsonAsync(admin, $"/api/v1/tasks/{taskId}");

        var failure = single.RootElement.GetProperty("failure");
        Assert.Equal("RemovedModuleFailure", failure.GetProperty("code").GetString());
        Assert.StartsWith(
            "The operation did not finish, and this panel has no description of why.",
            failure.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    /// <summary>A task that has not failed carries no failure at all.</summary>
    [Fact]
    public async Task A_task_that_has_not_failed_carries_no_failure()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var taskId = await SeedFailedTaskAsync(factory, failureCode: null);
        using var admin = await SignInSpeakingAsync(factory, "en");

        using var single = await GetJsonAsync(admin, $"/api/v1/tasks/{taskId}");

        Assert.Equal(JsonValueKind.Null, single.RootElement.GetProperty("failure").ValueKind);
    }

    /// <summary>Asserts a task payload carries the failure code and the expected sentence.</summary>
    /// <param name="task">The task's JSON.</param>
    /// <param name="sentence">The sentence expected.</param>
    private static void AssertFailure(JsonElement task, string sentence)
    {
        var failure = task.GetProperty("failure");
        Assert.Equal(FailureCode, failure.GetProperty("code").GetString());
        Assert.Equal(sentence, failure.GetProperty("message").GetString());
    }

    /// <summary>Migrates, seeds an administrator and stores one task, failed with the given code.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="failureCode">The code it failed with, or <c>null</c> to leave it running.</param>
    /// <returns>The task's id.</returns>
    private static async Task<Guid> SeedFailedTaskAsync(WebApplicationFactory<Program> factory, string? failureCode)
    {
        await EndpointHarness.MigrateAsync(factory);
        using var scope = factory.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await tasks.Database.MigrateAsync();
        await EndpointHarness.SeedAdministratorAsync(factory);

        var startedAt = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
        var task = new PanelTask(
            Guid.NewGuid(),
            TaskKinds.CertificateIssue,
            "shop.example.com",
            null,
            TaskAudience.AdministratorOnly,
            null,
            startedAt);
        if (failureCode is not null)
        {
            task.Fail(failureCode, startedAt.AddMinutes(1));
        }

        tasks.PanelTasks.Add(task);
        await tasks.SaveChangesAsync();

        return task.Id;
    }

    /// <summary>Signs the administrator in, asking for <paramref name="language"/> on every request.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="language">The <c>Accept-Language</c> the client sends.</param>
    /// <returns>The signed-in client.</returns>
    private static async Task<HttpClient> SignInSpeakingAsync(WebApplicationFactory<Program> factory, string language)
    {
        var client = await EndpointHarness.SignInAsync(factory, "admin");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        return client;
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    /// <returns>The parsed body.</returns>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Opens the task's stream and returns the payload of its first <c>task</c> frame.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="taskId">The task to watch.</param>
    /// <returns>The first frame's task payload.</returns>
    private static async Task<JsonElement> FirstStreamFrameAsync(HttpClient client, Guid taskId)
    {
        using var deadline = new CancellationTokenSource(ReadTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tasks/{taskId}/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
        var name = string.Empty;
        while (true)
        {
            var line = await reader.ReadLineAsync(deadline.Token).AsTask().WaitAsync(ReadTimeout, CancellationToken.None);
            if (line is null)
            {
                Assert.Fail("The stream ended before a task frame arrived.");
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
                continue;
            }

            if (name == "task" && line.StartsWith("data: ", StringComparison.Ordinal))
            {
                using var payload = JsonDocument.Parse(line["data: ".Length..]);
                return payload.RootElement.Clone();
            }
        }
    }
}
