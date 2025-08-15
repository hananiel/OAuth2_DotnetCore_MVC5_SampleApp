using System;
using System.Collections.Generic;

namespace OAuth2_CoreMVC_Sample.Models;

public class WebhookPayload
{
    public List<EventNotification> EventNotifications { get; set; }
}

public class EventNotification
{
    public string RealmId { get; set; }
    public DataChangeEvent DataChangeEvent { get; set; }
}

public class DataChangeEvent
{
    public List<Entity> Entities { get; set; }
}

public class Entity
{
    public string Name { get; set; }
    public string Id { get; set; }
    public string Operation { get; set; }
    public DateTime LastUpdated { get; set; } // Added to match your payload
}