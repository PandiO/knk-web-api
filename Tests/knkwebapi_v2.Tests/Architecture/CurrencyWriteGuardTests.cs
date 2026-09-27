using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Tests.Architecture;

/// <summary>
/// Currency ledger invariant 1 (docs/specs/currency-payments/DESIGN.md §3.1, IMPLEMENTATION_PLAN.md
/// Phase 2): only the ledger (Services/Currency) writes users.Coins/Gems/ExperiencePoints. Two
/// guards: EF itself refuses to write the columns (PropertySaveBehavior.Ignore — proven against
/// MySQL in Tests/MySql/LedgerRoutingMySqlTests), and this source scan fails the build's tests on
/// any new assignment outside the ledger, so a new feature can't quietly bypass it again.
/// </summary>
public class CurrencyWriteGuardTests
{
    /// <summary>x.Coins = …, x.Gems += …, x.ExperiencePoints++ (not ==, not =>).</summary>
    private static readonly Regex MemberWrite = new(
        @"\.(Coins|Gems|ExperiencePoints)\s*(\+\+|--|[-+*/]?=(?![=>]))", RegexOptions.Compiled);

    /// <summary>new User { … Coins = … } — an initializer that would only look like it sets a balance.</summary>
    private static readonly Regex UserInitializer = new(
        @"new\s+(Models\.)?User\s*(\(\s*\))?\s*\{[^{}]*\b(Coins|Gems|ExperiencePoints)\s*=(?![=>])", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] Excluded =
    {
        "Services/Currency/", // the ledger itself
        "Migrations/",
        "Tests/",
        "bin/",
        "obj/"
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("knkwebapi_v2.csproj not found above the test output folder.");
    }

    [Fact]
    public void NoCodeOutsideTheLedgerAssignsABalance()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Excluded.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }
            var source = File.ReadAllText(file);
            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//") || line.StartsWith("*"))
                {
                    continue;
                }
                if (MemberWrite.IsMatch(lines[i]))
                {
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
            foreach (Match match in UserInitializer.Matches(source))
            {
                var line = source[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{relative}:{line}: new User {{ … {match.Groups[3].Value} = … }}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Coins/Gems/ExperiencePoints may only be changed through ICurrencyService:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheScanFindsTheShapesItGuardsAgainst()
    {
        Assert.Matches(MemberWrite, "user.Coins = 5;");
        Assert.Matches(MemberWrite, "user.Gems -= price;");
        Assert.Matches(MemberWrite, "u.ExperiencePoints++;");
        Assert.DoesNotMatch(MemberWrite, "if (user.Coins == 5)");
        Assert.DoesNotMatch(MemberWrite, "var c = user.Coins;");
        Assert.Matches(UserInitializer, "new User { Username = \"x\", Coins = 250 }");
        Assert.DoesNotMatch(UserInitializer, "new UserDto { Coins = user.Coins }");
    }

    [Theory]
    [InlineData(nameof(User.Coins))]
    [InlineData(nameof(User.Gems))]
    [InlineData(nameof(User.ExperiencePoints))]
    public void EfNeverWritesTheBalanceColumns(string property)
    {
        using var ctx = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("write-guard").Options);
        var metadata = ctx.Model.FindEntityType(typeof(User))!.FindProperty(property)!;

        Assert.Equal(PropertySaveBehavior.Ignore, metadata.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Ignore, metadata.GetAfterSaveBehavior());
        Assert.Equal(0, metadata.GetDefaultValue());
    }
}
