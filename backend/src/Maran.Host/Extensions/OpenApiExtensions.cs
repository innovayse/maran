using Maran.Host.Cli.Commands;
using Maran.Host.Cli.Models;
using Maran.Host.OpenApi;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Microsoft.OpenApi.Models;

namespace Maran.Host.Extensions;

/// <summary>
/// Registers the OpenAPI generator, and only for a run that asks for the document. The running panel
/// has no OpenAPI services and no endpoint that serves one (rules/security.md item 10).
/// </summary>
public static class OpenApiExtensions
{
    /// <summary>
    /// Registers the generator when the command line is the <c>openapi</c> verb, and does nothing for an
    /// ordinary start or any other verb — the panel that serves requests carries none of this.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="invocation">The parsed command line, or null for an ordinary web-host start.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelOpenApi(this IServiceCollection services, CliInvocation? invocation)
    {
        if (invocation is { Verb: OpenApiCliCommand.VerbName })
        {
            services.AddPanelOpenApiGenerator();
        }

        return services;
    }

    /// <summary>
    /// Maps the panel's endpoints for the <c>openapi</c> verb, which never reaches the mapping a served start
    /// does. The API explorer describes an endpoint only once it is in the route table, so without this the
    /// document held the controller actions and none of the minimal endpoints (the module catalogue, the
    /// dashboard, the search). Nothing is served: the pipeline is built and never run.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <param name="invocation">The parsed command line.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapPanelOpenApiEndpoints(this WebApplication app, CliInvocation invocation)
    {
        if (invocation.Verb == OpenApiCliCommand.VerbName)
        {
            // The API explorer reads endpoints through the routing options, and only UseEndpoints copies a
            // builder's data sources into them. The application itself is never served on this verb, so the
            // endpoints are mapped on a separate pipeline branch that is built here and never run.
            var describing = ((IApplicationBuilder)app).New();
            describing.UseRouting();
            describing.UseEndpoints(endpoints => { endpoints.MapPanelEndpoints(); });
        }

        return app;
    }

    /// <summary>
    /// Registers the generator and the writer unconditionally. The tests call this directly to build
    /// the same document the verb writes; production reaches it only through
    /// <see cref="AddPanelOpenApi"/>.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPanelOpenApiGenerator(this IServiceCollection services)
    {
        // The minimal endpoints (the module catalogue, the dashboard, the search) are described by the
        // endpoint API explorer; the controller explorer alone does not see them.
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(
                OpenApiDocumentNames.Document,
                new OpenApiInfo
                {
                    Title = OpenApiDocumentNames.Title,
                    Version = OpenApiBuildVersion.Of(typeof(Program).Assembly),
                    Description = "Every endpoint of the panel API: the controller actions and the minimal "
                        + "endpoints (health, module catalogue, dashboard, search).",
                });

            options.CustomSchemaIds(OpenApiSchemaIds.Of);
            options.SupportNonNullableReferenceTypes();
            options.DescribeAllParametersInCamelCase();

            // The wire is what the serializer writes, not the shape of the C# type. A secret is written as
            // its plain string (SensitiveStringJsonConverter), never as an object with members.
            options.MapType<SensitiveString>(() => { return new OpenApiSchema { Type = "string" }; });
            options.SchemaFilter<PolymorphicSchemaFilter>();
            options.SchemaFilter<DashboardSectionSchemaFilter>();
            options.SchemaFilter<NullableReferenceSchemaFilter>();

            options.AddSecurityDefinition(
                OpenApiDocumentNames.BearerScheme,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "The access token returned by sign-in, sent as Authorization: Bearer <token>.",
                });
            options.AddSecurityDefinition(
                ApiKeyDefaults.Scheme,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = ApiKeyDefaults.HeaderName,
                    Description = "An API key, sent in the X-Api-Key header. Accepted only by operations that list it.",
                });

            options.OperationFilter<PanelSecurityOperationFilter>();
            options.DocumentFilter<DeterministicDocumentFilter>();
        });

        services.AddSingleton<OpenApiDocumentWriter>();
        return services;
    }
}
