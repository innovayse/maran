using Maran.ArchitectureTests.Fixtures;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that every operation a customer's token can reach is recorded in the customer
/// self-service matrix with the integration test class that proves another account's resource is answered
/// not found, so that the next customer endpoint cannot arrive without that question being asked (issue #147).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a checked matrix.</b> Each tenant module carries its own isolation test, but nothing made the
/// NEXT endpoint carry one: the view-as census asks what an operator viewing as a customer may do, never
/// what the customer may do. The matrix (<c>Baselines/customer-operations.txt</c>) is the answer, and this
/// class fails by name when an operation is missing from it, when an entry names an operation that no
/// longer exists, when an entry names a module other than the one that serves the operation, and when the
/// isolation test class an entry names is declared in no integration test file.
/// </para>
/// <para>
/// <b>Every refusal has a planted control and an accept-side control.</b> The real walk is satisfied by an
/// empty answer, so the checker is also handed <see cref="UnlistedCustomerController"/> (an operation no
/// matrix lists, which it must name) and <see cref="ListedCustomerController"/> (one listed operation and
/// three actions a customer does not reach, about which it must say nothing).
/// </para>
/// <para>
/// UNOBSERVED HERE: that the named test class asserts 404 for THIS operation. The check proves the class
/// exists; its content is the reviewer's. Also unobserved: minimal endpoints, and whether a plan feature
/// withholds a listed operation from a given customer at run time.
/// </para>
/// </remarks>
public sealed class CustomerOperationMatrixTests
{
    /// <summary>The key of the one operation the accept-side controller exposes to a customer.</summary>
    private const string ListedKey = "GET api/v1/architecture-tests/customer-listed/{id:guid}";

    /// <summary>Every operation a customer's token can reach is in the matrix.</summary>
    [Fact]
    public void Every_customer_reachable_operation_is_in_the_matrix()
    {
        var unlisted = CustomerOperationCensus.FindUnlisted(ComposedOperations(), CustomerOperationBaseline.Load());

        Assert.True(
            unlisted.Count == 0,
            "These operations are reachable by a customer and are not in " + CustomerOperationBaseline.RelativePath
            + ": " + string.Join("; ", unlisted)
            + ". Add a line (METHOD route, module, the integration test class holding the 'another account's "
            + "resource is answered not found' case) and the row in docs/accounts/customer-self-service.md. "
            + "UNOBSERVED HERE: that the named class asserts 404 for THIS operation; it proves the class exists, "
            + "the content is the reviewer's.");
    }

    /// <summary>Every matrix entry still names an operation a customer can reach.</summary>
    [Fact]
    public void The_matrix_names_only_operations_a_customer_can_still_reach()
    {
        var stale = CustomerOperationCensus.FindStale(ComposedOperations(), CustomerOperationBaseline.Load());

        Assert.True(
            stale.Count == 0,
            CustomerOperationBaseline.RelativePath + " has entries that no longer exist: " + string.Join("; ", stale));
    }

    /// <summary>Every matrix entry names the serving module and an isolation test class that exists, or gives a reason.</summary>
    [Fact]
    public void Every_matrix_entry_names_its_module_and_an_isolation_test_class_that_exists()
    {
        var problems = CustomerOperationCensus.FindUnproven(
            ComposedOperations(),
            CustomerOperationBaseline.Load(),
            IntegrationTestSources());

        Assert.True(
            problems.Count == 0,
            CustomerOperationBaseline.RelativePath + " has entries with no usable proof: " + string.Join("; ", problems)
            + ". UNOBSERVED HERE: that the named class asserts 404 for THIS operation.");
    }

    /// <summary>The walk reaches the tenant modules' operations, and the integration tests were really read.</summary>
    /// <remarks>
    /// The vacuity guard on the axis that can go blind: both the census and the file search are satisfied by
    /// finding nothing, so each must be shown to find what is known to be there.
    /// </remarks>
    [Fact]
    public void The_walk_reaches_the_tenant_modules_and_the_integration_tests_are_read()
    {
        var modules = ComposedOperations().Select(operation => { return operation.Module; }).ToHashSet();

        foreach (var module in new[] { "Sites", "Ssl", "Databases", "Cron", "Backups", "Sftp", "Ftp", "Mail", "Files", "Identity" })
        {
            Assert.Contains(module, modules);
        }

        Assert.Contains(
            IntegrationTestSources(),
            source => { return source.Contains("class SitesAuthorizationTests", StringComparison.Ordinal); });
    }

    /// <summary>The census names a customer operation the matrix omits, and leaves out what a customer cannot reach.</summary>
    /// <remarks>The positive control: without it the real walk is satisfied by a checker that finds nothing.</remarks>
    [Fact]
    public void The_census_names_a_customer_operation_the_matrix_omits()
    {
        var operations = CustomerOperationCensus.OperationsOf(new[] { typeof(UnlistedCustomerController) });

        var unlisted = CustomerOperationCensus.FindUnlisted(operations, Array.Empty<CustomerOperationEntry>());

        Assert.Equal(
            new List<string>
            {
                "DELETE api/v1/architecture-tests/customer-unlisted/{id:guid} "
                    + "(UnlistedCustomerController.DeleteAsync) is not in the matrix",
            },
            unlisted);
    }

    /// <summary>The census finds exactly the one operation a customer reaches in the accept-side controller.</summary>
    /// <remarks>The inverse control: a checker that lists everything, or nothing, fails here.</remarks>
    [Fact]
    public void The_census_accepts_a_listed_operation_and_ignores_actions_a_customer_cannot_reach()
    {
        var operations = CustomerOperationCensus.OperationsOf(new[] { typeof(ListedCustomerController) });
        var matrix = new[] { new CustomerOperationEntry(ListedKey, "Maran.ArchitectureTests", "SitesAuthorizationTests", string.Empty) };

        Assert.Equal(new List<string> { ListedKey }, operations.Select(operation => { return operation.Key; }));
        Assert.Empty(CustomerOperationCensus.FindUnlisted(operations, matrix));
        Assert.Empty(CustomerOperationCensus.FindStale(operations, matrix));
    }

    /// <summary>An entry that names an operation which no longer exists is reported.</summary>
    [Fact]
    public void The_census_reports_a_stale_matrix_entry()
    {
        var operations = CustomerOperationCensus.OperationsOf(new[] { typeof(ListedCustomerController) });
        var matrix = new[]
        {
            new CustomerOperationEntry(ListedKey, "Maran.ArchitectureTests", "SitesAuthorizationTests", string.Empty),
            new CustomerOperationEntry("GET api/v1/nowhere", "Maran.ArchitectureTests", "SitesAuthorizationTests", string.Empty),
        };

        Assert.Equal(
            new List<string> { "GET api/v1/nowhere is in the matrix but names no customer-reachable operation" },
            CustomerOperationCensus.FindStale(operations, matrix));
    }

    /// <summary>A wrong module, a missing isolation class and an exemption with no reason are each reported.</summary>
    [Fact]
    public void The_census_reports_an_entry_with_no_usable_proof()
    {
        var operations = CustomerOperationCensus.OperationsOf(new[] { typeof(ListedCustomerController) });
        var sources = new[] { "public sealed class RealIsolationTests { }" };

        var wrongModule = new CustomerOperationEntry(ListedKey, "Sites", "RealIsolationTests", string.Empty);
        var missingClass = new CustomerOperationEntry(ListedKey, "Maran.ArchitectureTests", "GhostIsolationTests", string.Empty);
        var noReason = new CustomerOperationEntry(ListedKey, "Maran.ArchitectureTests", null, " ");

        Assert.Equal(
            new List<string> { $"{ListedKey} is served by module Maran.ArchitectureTests, not Sites" },
            CustomerOperationCensus.FindUnproven(operations, new[] { wrongModule }, sources));
        Assert.Equal(
            new List<string> { $"{ListedKey} names GhostIsolationTests, which no integration test file declares" },
            CustomerOperationCensus.FindUnproven(operations, new[] { missingClass }, sources));
        Assert.Equal(
            new List<string> { $"{ListedKey} names no isolation test class and gives no reason" },
            CustomerOperationCensus.FindUnproven(operations, new[] { noReason }, sources));
    }

    /// <summary>An entry with a real class, or an exemption with a reason, produces no complaint.</summary>
    /// <remarks>The inverse control of <see cref="The_census_reports_an_entry_with_no_usable_proof"/>.</remarks>
    [Fact]
    public void The_census_accepts_a_proven_entry_and_a_reasoned_exemption()
    {
        var operations = CustomerOperationCensus.OperationsOf(new[] { typeof(ListedCustomerController) });
        var sources = new[] { "public sealed class RealIsolationTests { }" };

        var proven = new CustomerOperationEntry(ListedKey, "Maran.ArchitectureTests", "RealIsolationTests", string.Empty);
        var exempt = new CustomerOperationEntry(ListedKey, "Maran.ArchitectureTests", null, "acts on the caller's own login");

        Assert.Empty(CustomerOperationCensus.FindUnproven(operations, new[] { proven }, sources));
        Assert.Empty(CustomerOperationCensus.FindUnproven(operations, new[] { exempt }, sources));
    }

    /// <summary>The matrix file's parser reads both line shapes and refuses a malformed or repeated line by naming it.</summary>
    [Fact]
    public void The_matrix_parser_reads_both_line_shapes_and_refuses_a_broken_line()
    {
        var entries = CustomerOperationBaseline.Parse(
            "# comment\nGET a/b\tSites\tSitesAuthorizationTests\nPOST c\tIdentity\t-\town login\n");

        Assert.Equal("SitesAuthorizationTests", entries[0].IsolationClass);
        Assert.Null(entries[1].IsolationClass);
        Assert.Equal("own login", entries[1].Reason);

        Assert.Throws<FormatException>(() => { return CustomerOperationBaseline.Parse("GET a\tSites\n"); });
        Assert.Throws<FormatException>(() => { return CustomerOperationBaseline.Parse("GET a\tSites\t-\n"); });
        Assert.Throws<FormatException>(() => { return CustomerOperationBaseline.Parse("GET a\tS\tX\nGET a\tS\tX\n"); });
    }

    /// <summary>Every operation of the composed panel's controllers that a customer's token can reach.</summary>
    /// <returns>The census's answer for the real panel.</returns>
    private static List<CustomerOperation> ComposedOperations()
    {
        return CustomerOperationCensus.OperationsOf(ControllerAuthorizationCensus.ComposedControllers());
    }

    /// <summary>The text of every source file of the integration tests.</summary>
    /// <returns>One string per <c>.cs</c> file under <c>backend/tests/Maran.Host.IntegrationTests</c>, build output excluded.</returns>
    private static List<string> IntegrationTestSources()
    {
        var folder = Path.Combine(SpaLocaleKeys.RepositoryRoot(), "backend", "tests", "Maran.Host.IntegrationTests");

        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                return !path.Contains($"{separator}obj{separator}") && !path.Contains($"{separator}bin{separator}");
            })
            .Select(File.ReadAllText)
            .ToList();
    }
}
