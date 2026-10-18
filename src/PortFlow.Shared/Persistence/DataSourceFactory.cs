using Npgsql;
using PortFlow.Shared.Config;

namespace PortFlow.Shared.Persistence;

internal static class DataSourceFactory
{
    private const string JdbcPrefix = "jdbc:postgresql://";

    public static NpgsqlDataSource Create(PortFlowOptions.DatasourceOptions options)
    {
        var builder = new NpgsqlDataSourceBuilder(ToConnectionString(options));
        builder.UseNodaTime();
        return builder.Build();
    }

    public static string ToConnectionString(PortFlowOptions.DatasourceOptions options)
    {
        var csb = options.Url.StartsWith(JdbcPrefix, StringComparison.Ordinal)
            ? FromJdbcUrl(options.Url)
            : new NpgsqlConnectionStringBuilder(options.Url);
        if (string.IsNullOrEmpty(csb.Username))
        {
            csb.Username = options.Username;
        }
        if (string.IsNullOrEmpty(csb.Password))
        {
            csb.Password = options.Password;
        }
        csb.MaxPoolSize = options.MaximumPoolSize;
        return csb.ConnectionString;
    }

    // jdbc:postgresql://host[:port]/database[?user=..&password=..]
    private static NpgsqlConnectionStringBuilder FromJdbcUrl(string url)
    {
        var uri = new Uri("postgresql://" + url[JdbcPrefix.Length..]);
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
        };
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var value = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            switch (kv[0])
            {
                case "user":
                    csb.Username = value;
                    break;
                case "password":
                    csb.Password = value;
                    break;
            }
        }
        return csb;
    }
}
