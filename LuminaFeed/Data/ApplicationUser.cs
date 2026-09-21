using LuminaFeed.Domain;
using Microsoft.AspNetCore.Identity;

namespace LuminaFeed.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    /// <summary>Distinguishes admin users from standard users (drives the "Admin" policy in G0.4).</summary>
    public bool IsAdmin { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
