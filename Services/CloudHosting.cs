using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;

namespace AcxiomCRM.Services;

/// <summary>Explicit managed-host production settings; local Development behavior stays unchanged.</summary>
public static class CloudHosting
{
    public static bool Configure(WebApplicationBuilder builder)
    {
        var port = builder.Configuration["PORT"];
        if (!string.IsNullOrEmpty(port))
        {
            if (!int.TryParse(port, out var number) || number is < 1 or > 65535)
                throw new InvalidOperationException("PORT must be a valid TCP port.");
            builder.WebHost.UseUrls($"http://0.0.0.0:{number}");
        }

        var onRailway = builder.Environment.IsProduction() && !string.IsNullOrWhiteSpace(builder.Configuration["RAILWAY_SERVICE_ID"]);
        var onRender = builder.Environment.IsProduction() && builder.Configuration.GetValue<bool>("RENDER");
        if (!onRailway && !onRender) return false;
        if (onRailway && onRender) throw new InvalidOperationException("Configure only one cloud hosting provider.");
        var provider = onRender ? "Render" : "Railway";

        var volume = builder.Configuration[onRender ? "Hosting:DataPath" : "RAILWAY_VOLUME_MOUNT_PATH"];
        if (string.IsNullOrWhiteSpace(volume) || !Path.IsPathFullyQualified(volume) || !Directory.Exists(volume))
            throw new InvalidOperationException($"Attach a {provider} persistent disk/volume and configure its path before starting this application.");
        var keys = Path.Combine(volume, "keys");
        Directory.CreateDirectory(keys);
        builder.Configuration["ConnectionStrings:DefaultConnection"] = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(volume, "acxiom.db"), ForeignKeys = true
        }.ToString();
        builder.Services.AddDataProtection().SetApplicationName("AcxiomCRM").PersistKeysToFileSystem(new DirectoryInfo(keys));

        var hosts = (builder.Configuration["AllowedHosts"] ?? "localhost;127.0.0.1").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (onRailway) hosts.Add("healthcheck.railway.app");
        var domainKey = onRender ? "RENDER_EXTERNAL_HOSTNAME" : "RAILWAY_PUBLIC_DOMAIN";
        var publicDomain = builder.Configuration[domainKey];
        if (!string.IsNullOrWhiteSpace(publicDomain))
        {
            if (Uri.CheckHostName(publicDomain) != UriHostNameType.Dns)
                throw new InvalidOperationException($"{domainKey} must be a DNS hostname without a scheme or path.");
            hosts.Add(publicDomain);
        }
        builder.Configuration["AllowedHosts"] = string.Join(';', hosts.Distinct(StringComparer.OrdinalIgnoreCase));
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);

        if (!builder.Configuration.GetValue<bool>($"Hosting:{provider}Proxy"))
            throw new InvalidOperationException($"Enable Hosting__{provider}Proxy for {provider}'s managed HTTP ingress.");
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Managed ingress peer IPs are not fixed. This provider-specific opt-in relies on its
            // managed HTTP edge and an isolated service network; never expose a raw TCP proxy.
            // Trust only the last scheme header. Never consume caller-supplied host or IP.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });
        return true;
    }
}
