// Universal namespaces for Maran.Host, the composition root. Microsoft.NET.Sdk.Web already
// contributes its own implicit global usings (Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http,
// Microsoft.AspNetCore.Routing, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Hosting,
// Microsoft.Extensions.Configuration, Microsoft.Extensions.Logging, plus the base BCL set); only
// this project's own namespaces and the few framework ones not already implicit are listed here.

// Each of this project's own namespaces is added here as its folder gains its first type;
// a global using for an empty folder does not compile (CS0234).
global using Maran.SharedKernel.Interfaces;
