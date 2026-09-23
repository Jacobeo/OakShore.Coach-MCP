using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Xunit;

namespace Coach.ArchitectureTests;

public sealed class DependencyTests
{
    [Theory]
    [InlineData("Coach.Domain", "Coach.Infrastructure")]
    [InlineData("Coach.Domain", "Coach.Api")]
    [InlineData("Coach.Infrastructure", "Coach.Api")]
    [InlineData("Coach.Domain", "Microsoft.EntityFrameworkCore")]
    [InlineData("Coach.Domain", "Microsoft.AspNetCore")]
    [InlineData("Coach.Domain", "ModelContextProtocol")]
    [InlineData("Coach.Domain", "Microsoft.Data.SqlClient")]
    [InlineData("Coach.Domain", "System.Text.Json.Serialization")]
    [InlineData("Coach.Domain", "System.ComponentModel.DataAnnotations")]
    [InlineData("Coach.Api", "Microsoft.EntityFrameworkCore")]
    [InlineData("Coach.Api", "Microsoft.Data.SqlClient")]
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
        var project = XDocument.Load(Path.Combine(root.FullName, "src/Coach.Domain/Coach.Domain.csproj"));
        Assert.Empty(project.Descendants("PackageReference"));
    }
}
