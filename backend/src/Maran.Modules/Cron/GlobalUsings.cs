// Universal namespaces for Maran.Modules.Cron: the module's own core namespaces plus the small
// set of framework namespaces every operation folder needs. Not a place for module-specific
// imports that only a handful of files use (rules/csharp.md "GlobalUsings.cs").
//
// Microsoft.EntityFrameworkCore is absent, and stays absent: this module has no persistence at all
// (see Maran.Modules.Cron.csproj), because the account's crontab is the record.

global using System;
global using Maran.SharedKernel.Interfaces;
global using Maran.SharedKernel.Results;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
