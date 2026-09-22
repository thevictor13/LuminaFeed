using LuminaFeed.Services.Notifications;

namespace LuminaFeed.Tests;

/// <summary>The channel enumeration the subscription dialog (C1) and the dispatcher (C4) key on.</summary>
public class NotificationChannelTests
{
    [Fact]
    public void Lists_EmailAndSlack_InThatOrder()
    {
        Assert.Equal([NotificationChannel.Email, NotificationChannel.Slack], NotificationChannel.List.OrderBy(c => c.Value));
    }

    [Theory]
    [InlineData("Email", 1)]
    [InlineData("Slack", 2)]
    public void ResolvesByName_AndByValue(string name, int value)
    {
        Assert.Same(NotificationChannel.FromName(name), NotificationChannel.FromValue(value));
        Assert.Equal(name, NotificationChannel.FromValue(value).Name);
    }
}
