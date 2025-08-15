using System.Collections.Concurrent;
using System.Text;

public class WebhookDataStore
{
    // Singleton instance
    private static readonly WebhookDataStore _instance = new WebhookDataStore();
    public static WebhookDataStore Instance => _instance;

    // Thread-safe storage: key is realmId, value is WebhookInfo
    private readonly ConcurrentQueue<string> _webhookInfo = new();

    // Private constructor to enforce singleton
    private WebhookDataStore() { }

    // Store or update webhook info for a realmId
    public void StoreInfo( string info)
    {
        _webhookInfo.Enqueue(info);
    }

    // Retrieve webhook info for a realmId
    public string GetAllInfo()
    {
        var allInfo = new StringBuilder();
        foreach (var entry in _webhookInfo)
        {
            allInfo.AppendLine(entry + "<br/>");
        }

        return allInfo.ToString();
    }
}