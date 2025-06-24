using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI; // Ensure this namespace is included
using Microsoft.InformationProtection;
using MipSdkDotnetNext;
using MipSdkDotnetNext.Services;
using MipSdkDotnetNext.Utilities;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Load config from appsettings.json or environment
var doCertAuth = builder.Configuration.GetValue<bool>("AzureAd:DoCertAuth");

// Common Identity setup
var identitySection = builder.Configuration.GetSection("AzureAd");

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(identitySection, OpenIdConnectDefaults.AuthenticationScheme)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// Add UI support (if using [Authorize] Razor Pages)
builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI(); // Ensure Microsoft.Identity.Web.UI is referenced in your project

// Inject HttpContext for OBO flow
builder.Services.AddHttpContextAccessor();

// Inject your services
builder.Services.AddScoped<IAuthDelegate, AuthDelegateImplementation>();
builder.Services.AddScoped<FileApi>();
builder.Services.AddSingleton<ApplicationInfo>(sp => new ApplicationInfo
{
    ApplicationId = builder.Configuration["AzureAd:ClientId"],
    ApplicationName = builder.Configuration["Application:Name"],
    ApplicationVersion = builder.Configuration["Application:Version"]
});
var app = builder.Build();

// Middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Razor Pages & (optional) Controllers
app.MapRazorPages();
//app.MapControllers(); // Uncomment if using controllers

app.Run();
