using Newtonsoft.Json.Serialization;
using OpenSearch.Client;
using OpenSearch.Client.JsonNetSerializer;
using OpenSearch.Net;

namespace Kp.Ms.Sms.Extensions;

public static class OpenSearchExtension
{
    public static void AddOpenSearch(this IServiceCollection services, IConfiguration configuration)
    {
        string? openSearchUrl = configuration["Settings:OpenSearch:Url"];
        if (string.IsNullOrEmpty(openSearchUrl))
        {
            throw new Exception("OpenSearch url is empty");
        }

        string? openSearchUser = configuration["Settings:OpenSearch:User"];
        if (string.IsNullOrEmpty(openSearchUser))
        {
            throw new Exception("OpenSearch user is empty");
        }

        string? openSearchPassword = configuration["Settings:OpenSearch:Password"];
        if (string.IsNullOrEmpty(openSearchPassword))
        {
            throw new Exception("OpenSearch password is empty");
        }

        var pool = new SingleNodeConnectionPool(new Uri(openSearchUrl));
        var connectionSettings = new ConnectionSettings(pool, (builtin, settings) =>
                new JsonNetSerializer(builtin, settings,
                    modifyContractResolver: c => c.NamingStrategy = new SnakeCaseNamingStrategy()
                )
            )
            .ServerCertificateValidationCallback((_, _, _, _) => true)
            .BasicAuthentication(openSearchUser, openSearchPassword);
        var client = new OpenSearchClient(connectionSettings);

        services.AddSingleton(client);
    }
    public static string GetSmsStorageName(this IConfiguration configuration)
    {
        return configuration["Settings:Storages:Sms"] ??
              throw new Exception("Sms storage name not configured");
    }

    public static string GetCallStorageName(this IConfiguration configuration)
    {
        return configuration["Settings:Storages:Call"] ??
              throw new Exception("Call storage name not configured");
    }

}