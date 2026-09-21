using FluentValidation;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LuminaFeed.Authorization;
using LuminaFeed.Components;
using LuminaFeed.Components.Account;
using LuminaFeed.Data;
using LuminaFeed.Data.Seed;
using LuminaFeed.Options;
using LuminaFeed.Services.Categories;
using LuminaFeed.Services.Email;
using LuminaFeed.Services.Feeds;
using LuminaFeed.Services.Notifications;
using LuminaFeed.Services.Polling;
using LuminaFeed.Services.Subscriptions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                       throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// A factory rather than a plain AddDbContext: Interactive Server circuits and the polling background
// service outlive a request, so application services create a short-lived context per operation.
// AddDbContextFactory also registers a scoped ApplicationDbContext, which Identity and the seeders use.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<AdminUserSeeder>();

// Application services (ErrorOr results, FluentValidation request validators).
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IFeedService, FeedService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Feed polling: a typed HttpClient fetcher, one scoped polling pass, and the background loop that drives it.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IFeedFetcher, HttpFeedFetcher>(HttpFeedFetcher.Configure)
    .ConfigurePrimaryHttpMessageHandler(HttpFeedFetcher.CreateHandler);
builder.Services.AddScoped<IFeedPollingService, FeedPollingService>();
builder.Services.AddHostedService<FeedPollingBackgroundService>();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AdminClaimsPrincipalFactory>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminAuthorization.PolicyName, policy =>
        policy.RequireClaim(AdminAuthorization.ClaimType, AdminAuthorization.ClaimValue));

// Real email delivery via MailKit (replaces the template's no-op sender).
builder.Services.AddSingleton<IMailSender, MailKitMailSender>();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityEmailSender>();

// New-article notifications: one INotificationService per channel (Slack joins with C2).
builder.Services.AddSingleton<INotificationService, EmailNotificationService>();

// Strongly-typed configuration, validated at startup.
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<PollingOptions>()
    .Bind(builder.Configuration.GetSection(PollingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<UnsubscribeOptions>()
    .Bind(builder.Configuration.GetSection(UnsubscribeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
// Optional bootstrap admin account (no validation: empty means "no admin seeded").
builder.Services.AddOptions<AdminSeedOptions>()
    .Bind(builder.Configuration.GetSection(AdminSeedOptions.SectionName));

var app = builder.Build();

// Apply migrations and seed the researched feed catalogue (G0.7).
// NOTE: auto-migrating on startup is a deliberate temporary shortcut and is NOT production-ready
// (no review gate, races across instances, no rollback). Revisit before any production deployment.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    // Migration + seeding run to completion during startup; ApplicationStopping signals shutdown,
    // not a startup deadline, so it is not the right token to cancel this work.
    await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<DatabaseSeeder>().SeedAsync(CancellationToken.None);
    await services.GetRequiredService<AdminUserSeeder>().SeedAsync(CancellationToken.None);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
