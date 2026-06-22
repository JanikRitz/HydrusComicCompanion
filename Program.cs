using MudBlazor.Services;
using HydrusComicCompanion.Components;
using HydrusComicCompanion.Models;
using HydrusComicCompanion.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

var dataProtectionKeysDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(dataProtectionKeysDirectory);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));

builder.Services.Configure<HydrusSettings>(builder.Configuration.GetSection("HydrusSettings"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IHydrusSettingsService, HydrusSettingsService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
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

app.Run();
