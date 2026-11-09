namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// Project-file texts of planted packageable modules — the violation <c>ModuleSdkOnlyReferenceTests</c>
/// must flag by rule name, and the clean file that must not be.
/// </summary>
/// <remarks>
/// Text and not a real project: a project that really referenced the Host would be a circular
/// reference the solution could not build, which is the point of the rule and also why the plant is
/// a string the law reads exactly as it reads a real <c>.csproj</c>.
/// </remarks>
public static class PlantedPackageReferencingHost
{
    /// <summary>A packageable module's project file that obeys the reference contract.</summary>
    public const string CleanProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="..\..\Maran.Agent.Client\Maran.Agent.Client.csproj" />
            <ProjectReference Include="..\..\Maran.Sdk\Maran.Sdk.csproj" />
            <ProjectReference Include="..\..\Maran.SharedKernel\Maran.SharedKernel.csproj" />
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="WolverineFx" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>A project file that references the Host, another module, an unlisted package and a bare assembly.</summary>
    public const string BadProject = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <ItemGroup>
            <FrameworkReference Include="Microsoft.WindowsDesktop.App" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="..\..\Maran.Host\Maran.Host.csproj" />
            <ProjectReference Include="..\Accounts\Maran.Modules.Accounts.csproj" />
            <ProjectReference Include="..\..\Maran.Sdk\Maran.Sdk.csproj" />
          </ItemGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" />
            <Reference Include="Vendor.Native" />
          </ItemGroup>
        </Project>
        """;
}
