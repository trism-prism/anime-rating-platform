namespace api.Services;

public static class DatabaseUrl
{
    // Railway gives postgres://user:pass@host:port/db; Npgsql wants key=value pairs.
    public static string ToNpgsql(string url)
    {
        var uri = new Uri(url);
        var login = uri.UserInfo.Split(':', 2);
        return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(login[0])};Password={Uri.UnescapeDataString(login.Length > 1 ? login[1] : "")};" +
               "SSL Mode=Require;Trust Server Certificate=true";
    }
}
