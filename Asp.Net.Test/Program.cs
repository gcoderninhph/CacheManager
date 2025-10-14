using CacheManager;
using CacheManager.Core;
using Asp.Net.Test.Services;
using Asp.Net.Test.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers(); // Add Controllers support

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CacheManager API", Version = "v1" });
});

// Add CacheManager from appsettings.json
builder.Services.AddCacheManager(builder.Configuration);

// Register background service for cache registration
builder.Services.AddHostedService<CacheRegistrationBackgroundService>();

var app = builder.Build();

// Configure Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CacheManager API v1");
    });
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

// Enable CacheManager Dashboard from appsettings.json
app.UseCacheManagerDashboard();

// Map Controllers
app.MapControllers();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

