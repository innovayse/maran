using System.Reflection;
using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// Pins the property this repository's mail split exists for: nothing that needs to send mail
/// depends on the Monitoring module.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect being pinned.</b> The SMTP settings, the sender and the handler for
/// <see cref="SendMailRequested"/> used to live in Monitoring, because the alert evaluator was the
/// first thing that wanted to send. That made Identity's password reset — a security feature —
/// silently depend on Monitoring being loaded and configured: remove or disable that module and the
/// reset event reached a queue with no handler, with nothing to tell the operator.
/// </para>
/// <para>
/// <b>What these tests can show, and what they cannot.</b> The panel composes its modules from one
/// static list (<c>ModuleRegistry.All</c>), so no test in this repository can boot a host with
/// Monitoring genuinely absent — there is no configuration switch to turn it off. What is checked
/// instead is the property that absence would exercise: that the handler is in another assembly,
/// that removing Monitoring's assembly from the module set still leaves one, and that Monitoring
/// carries no mail machinery of its own. That is strictly weaker than a boot without Monitoring: it
/// proves the dependency is not in the CODE, not that a host stripped of Monitoring starts. A
/// registration mistake in <c>NotificationsModule.ConfigureServices</c> would pass here and be caught
/// by the integration suite instead, where the reset endpoint really does send.
/// </para>
/// </remarks>
public sealed class MailFacilityOwnershipTests
{
    /// <summary>The assembly name of the module that must NOT own mail.</summary>
    private const string MonitoringAssembly = "Maran.Modules.Monitoring";

    /// <summary>Handling a message is a public HandleAsync taking it as the first parameter.</summary>
    private const string HandlerMethod = "HandleAsync";

    /// <summary>Exactly one module handles the panel's request to send mail.</summary>
    /// <remarks>
    /// Two handlers would mean every password reset produced two mails, and one would mean the
    /// second was written without anybody noticing the first.
    /// </remarks>
    [Fact]
    public void Exactly_one_module_handles_the_request_to_send_mail()
    {
        var handlers = MailHandlerAssemblies();

        Assert.Single(handlers);
    }

    /// <summary>The module that sends mail is not the monitoring module.</summary>
    [Fact]
    public void The_module_that_sends_mail_is_not_the_monitoring_module()
    {
        var handlers = MailHandlerAssemblies();

        Assert.DoesNotContain(MonitoringAssembly, handlers);
    }

    /// <summary>Mail is still handled when the monitoring assembly is taken out of the module set.</summary>
    /// <remarks>
    /// The closest this suite can come to the real question — "does password reset still work with
    /// Monitoring gone" — without a host that can compose modules selectively. It reddens the moment
    /// the sender moves back into Monitoring, which is the regression worth catching.
    /// </remarks>
    [Fact]
    public void Mail_is_still_handled_when_the_monitoring_assembly_is_removed()
    {
        var withoutMonitoring = MailHandlerAssemblies()
            .Where(name =>
            {
                return !string.Equals(name, MonitoringAssembly, StringComparison.Ordinal);
            })
            .ToList();

        Assert.NotEmpty(withoutMonitoring);
    }

    /// <summary>The monitoring module carries no mail machinery of its own.</summary>
    /// <remarks>
    /// The mailer seam, the settings entity and the settings cache all left with the facility. A type
    /// named for SMTP reappearing here means somebody has started a second mail implementation inside
    /// the module the first one was moved out of.
    /// </remarks>
    [Fact]
    public void The_monitoring_module_carries_no_mail_machinery()
    {
        var monitoring = ModuleAssemblies().Single(assembly =>
        {
            return assembly.GetName().Name == MonitoringAssembly;
        });

        var mailTypes = monitoring.GetTypes()
            .Where(type =>
            {
                return type.Name.Contains("Smtp", StringComparison.Ordinal)
                    || type.Name.Contains("Mailer", StringComparison.Ordinal);
            })
            .Select(type =>
            {
                return type.FullName ?? type.Name;
            })
            .ToList();

        Assert.True(
            mailTypes.Count == 0,
            "The Monitoring module declares mail types again: " + string.Join(", ", mailTypes));
    }

    /// <summary>Names the module assemblies that declare a handler for the send-mail request.</summary>
    /// <returns>The assembly simple names, without duplicates.</returns>
    private static List<string> MailHandlerAssemblies()
    {
        var assemblies = ModuleAssemblies();
        Assert.NotEmpty(assemblies);

        return assemblies
            .Where(assembly =>
            {
                return assembly.GetTypes().Any(HandlesSendMail);
            })
            .Select(assembly =>
            {
                return assembly.GetName().Name!;
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Tells whether one type is a handler for <see cref="SendMailRequested"/>.</summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><c>true</c> when it declares a public <c>HandleAsync</c> taking the message.</returns>
    private static bool HandlesSendMail(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method =>
            {
                return method.Name == HandlerMethod
                    && method.GetParameters().Length > 0
                    && method.GetParameters()[0].ParameterType == typeof(SendMailRequested);
            });
    }

    /// <summary>Every loaded module assembly.</summary>
    /// <returns>The assemblies whose simple name starts with the module prefix.</returns>
    /// <remarks>
    /// Loaded off disk rather than written out by hand — the same idiom
    /// <see cref="ModuleCoverageTests"/> uses — so a module added later is covered without anyone
    /// extending a list, and a module assembly nothing has touched yet is still seen. That suite is
    /// what guarantees the assemblies are present at all, so an empty answer here is a broken suite
    /// rather than a passing one.
    /// </remarks>
    private static List<Assembly> ModuleAssemblies()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maran.Modules.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // Native or mixed-mode files matching the pattern are not managed assemblies.
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                return assembly.GetName().Name?.StartsWith("Maran.Modules.", StringComparison.Ordinal) == true;
            })
            .ToList();
    }
}
