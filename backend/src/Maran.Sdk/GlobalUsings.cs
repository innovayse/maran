// Universal namespaces for Maran.Sdk: the module contract surface every module (ours and
// third-party) builds controllers and registration against. Maran.Sdk is a plain class
// library (not Microsoft.NET.Sdk.Web), so — unlike Maran.Host — it does not get ASP.NET
// Core's implicit global usings for free; the ones it actually needs are listed explicitly here.

global using System;
global using Maran.SharedKernel.Interfaces;
global using Maran.SharedKernel.Results;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
