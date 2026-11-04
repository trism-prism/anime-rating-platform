using api.Services;

namespace api.Tests;

public class DatabaseUrlTests
{
    [Fact]
    public void Railway_url_becomes_npgsql_connection_string()
    {
        var result = DatabaseUrl.ToNpgsql("postgresql://bob:p%40ss@db.example.com:5433/railway");
        Assert.Equal(
            "Host=db.example.com;Port=5433;Database=railway;Username=bob;Password=p@ss;SSL Mode=Require;Trust Server Certificate=true",
            result);
    }
}
