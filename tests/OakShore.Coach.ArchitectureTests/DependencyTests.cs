using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Xunit;

namespace OakShore.Coach.ArchitectureTests;

public sealed class DependencyTests
{
    [Theory]
    [InlineData("OakShore.Coach.Domain", "OakShore.Coach.Infrastructure")]
    [InlineData("OakShore.Coach.Domain", "OakShore.Coach.Api")]
    [InlineData("OakShore.Coach.Infrastructure", "OakShore.Coach.Api")]
    [InlineData("OakShore.Coach.Domain", "Microsoft.EntityFrameworkCore")]
    [InlineData("OakShore.Coach.Domain", "Microsoft.AspNetCore")]
    [InlineData("OakShore.Coach.Domain", "ModelContextProtocol")]
    [InlineData("OakShore.Coach.Domain", "Microsoft.Data.SqlClient")]
    [InlineData("OakShore.Coach.Domain", "System.Text.Json.Serialization")]
    [InlineData("OakShore.Coach.Domain", "System.ComponentModel.DataAnnotations")]
    [InlineData("OakShore.Coach.Api", "Microsoft.EntityFrameworkCore")]
    [InlineData("OakShore.Coach.Api", "Microsoft.Data.SqlClient")]
    public void Dependencies_point_inward(string assembly, string forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(assembly))
            .ShouldNot().HaveDependencyOn(forbidden).GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_has_no_package_references()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "coach-mcp.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var project = XDocument.Load(Path.Combine(root.FullName, "src/OakShore.Coach.Domain/OakShore.Coach.Domain.csproj"));
        Assert.Empty(project.Descendants("PackageReference"));
    }
}
