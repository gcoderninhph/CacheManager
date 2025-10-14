using CacheManager;
using CacheManager.Core;
using Asp.Net.Test.Services;
using Asp.Net.Test.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

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

// ==================== CRUD API for Map ====================

/// <summary>
/// Get a value from map by key
/// </summary>
app.MapGet("/api/map/{mapName}/{key}", async (string mapName, string key, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        var value = await map.GetValueAsync(key);
        return Results.Ok(new { key, value });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = $"Key '{key}' not found in map '{mapName}'" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("GetMapValue")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Set a value in map
/// </summary>
app.MapPost("/api/map/{mapName}/{key}", async (string mapName, string key, string value, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        await map.SetValueAsync(key, value);
        return Results.Ok(new { message = "Value set successfully", key, value });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("SetMapValue")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Update a value in map (same as Set)
/// </summary>
app.MapPut("/api/map/{mapName}/{key}", async (string mapName, string key, string value, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        await map.SetValueAsync(key, value);
        return Results.Ok(new { message = "Value updated successfully", key, value });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("UpdateMapValue")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Delete all data in map
/// </summary>
app.MapDelete("/api/map/{mapName}", async (string mapName, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, string>(mapName);
        await map.ClearAsync();
        return Results.Ok(new { message = $"Map '{mapName}' cleared successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("ClearMap")
.WithTags("Map CRUD")
.WithOpenApi();

/// <summary>
/// Get all values from map (with pagination)
/// </summary>
app.MapGet("/api/map/{mapName}", async (string mapName, ICacheStorage storage, int page = 1, int pageSize = 20) =>
{
    try
    {
        var mapInstance = storage.GetMapInstance(mapName);
        if (mapInstance == null)
        {
            return Results.NotFound(new { error = $"Map '{mapName}' not found" });
        }

        var method = mapInstance.GetType().GetMethod("GetAllEntriesAsync");
        if (method != null)
        {
            var task = method.Invoke(mapInstance, null) as Task;
            if (task != null)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                var allEntries = resultProperty?.GetValue(task) as IEnumerable<object>;
                
                if (allEntries != null)
                {
                    var entriesList = allEntries.ToList();
                    var totalCount = entriesList.Count;
                    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                    
                    var pagedEntries = entriesList
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                    
                    return Results.Ok(new 
                    { 
                        mapName,
                        entries = pagedEntries,
                        pagination = new
                        {
                            currentPage = page,
                            pageSize,
                            totalCount,
                            totalPages
                        }
                    });
                }
            }
        }
        
        return Results.Problem("Unable to retrieve map data");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("GetAllMapValues")
.WithTags("Map CRUD")
.WithOpenApi();

// ==================== CRUD API for UserInfo ====================

/// <summary>
/// Get a user by userId
/// </summary>
app.MapGet("/api/userinfo/{userId}", async (string userId, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, UserInfo>("user-info");
        var userInfo = await map.GetValueAsync(userId);
        return Results.Ok(userInfo);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = $"User '{userId}' not found" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("GetUserInfo")
.WithTags("UserInfo CRUD")
.WithOpenApi();

/// <summary>
/// Create or update a user
/// </summary>
app.MapPost("/api/userinfo", async (UserInfo userInfo, ICacheStorage storage) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(userInfo.UserId))
        {
            return Results.BadRequest(new { error = "UserId is required" });
        }

        var map = storage.GetMap<string, UserInfo>("user-info");
        await map.SetValueAsync(userInfo.UserId, userInfo);
        return Results.Ok(new { message = "User created/updated successfully", userInfo });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("CreateOrUpdateUserInfo")
.WithTags("UserInfo CRUD")
.WithOpenApi();

/// <summary>
/// Update a user
/// </summary>
app.MapPut("/api/userinfo/{userId}", async (string userId, UserInfo userInfo, ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, UserInfo>("user-info");
        
        // Check if user exists
        try
        {
            await map.GetValueAsync(userId);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"User '{userId}' not found" });
        }

        // Update user
        userInfo.UserId = userId; // Ensure userId matches route
        await map.SetValueAsync(userId, userInfo);
        return Results.Ok(new { message = "User updated successfully", userInfo });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("UpdateUserInfo")
.WithTags("UserInfo CRUD")
.WithOpenApi();

/// <summary>
/// Delete a user
/// </summary>
app.MapDelete("/api/userinfo/{userId}", async (string userId, ICacheStorage storage) =>
{
    try
    {
        // Note: IMap doesn't have RemoveAsync, only ClearAsync
        // For individual deletion, we need to use Redis directly or use a workaround
        // For now, return NotImplemented
        return Results.Json(new 
        { 
            error = "Individual user deletion not supported",
            message = "Use DELETE /api/userinfo to clear all users, or implement custom Redis delete"
        }, statusCode: 501);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("DeleteUserInfo")
.WithTags("UserInfo CRUD")
.WithOpenApi();

/// <summary>
/// Get all users (with pagination)
/// </summary>
app.MapGet("/api/userinfo", async (ICacheStorage storage, int page = 1, int pageSize = 20) =>
{
    try
    {
        var mapInstance = storage.GetMapInstance("user-info");
        if (mapInstance == null)
        {
            return Results.NotFound(new { error = "UserInfo map not found" });
        }

        var method = mapInstance.GetType().GetMethod("GetEntriesPagedAsync");
        if (method != null)
        {
            var task = method.Invoke(mapInstance, new object[] { page, pageSize, null! }) as Task;
            if (task != null)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty("Result");
                var pagedResult = resultProperty?.GetValue(task);
                
                return Results.Ok(new 
                { 
                    mapName = "user-info",
                    data = pagedResult
                });
            }
        }
        
        return Results.Problem("Unable to retrieve user data");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("GetAllUserInfo")
.WithTags("UserInfo CRUD")
.WithOpenApi();

/// <summary>
/// Clear all users
/// </summary>
app.MapDelete("/api/userinfo", async (ICacheStorage storage) =>
{
    try
    {
        var map = storage.GetMap<string, UserInfo>("user-info");
        await map.ClearAsync();
        return Results.Ok(new { message = "All users cleared successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
})
.WithName("ClearAllUserInfo")
.WithTags("UserInfo CRUD")
.WithOpenApi();

// ==================== Test Data Endpoint ====================

/// <summary>
/// Add sample test data to maps
/// </summary>
app.MapGet("/test/add-data", async (ICacheStorage storage) =>
{
    var userSessions = storage.GetMap<string, string>("user-sessions");
    
    // Add more than 20 records to test pagination
    for (int i = 1; i <= 50; i++)
    {
        await userSessions.SetValueAsync($"user{i}", $"session-token-{Guid.NewGuid().ToString().Substring(0, 8)}");
    }
    
    var userData = storage.GetMap<int, string>("user-data");
    for (int i = 1; i <= 30; i++)
    {
        await userData.SetValueAsync(i, $"{{\"name\":\"User{i}\",\"email\":\"user{i}@example.com\"}}");
    }
    
    // Add UserInfo test data
    var userInfoMap = storage.GetMap<string, UserInfo>("user-info");
    var names = new[] { "Alice", "Bob", "Charlie", "David", "Eve", "Frank", "Grace", "Henry", "Ivy", "Jack" };
    for (int i = 1; i <= 25; i++)
    {
        await userInfoMap.SetValueAsync($"user-{i:000}", new UserInfo
        {
            UserId = $"user-{i:000}",
            Name = names[(i - 1) % names.Length] + i,
            Age = 20 + (i % 50)
        });
    }
    
    return Results.Json(new 
    { 
        success = true, 
        message = "Test data added: 50 user-sessions, 30 user-data, 25 user-info!" 
    });
})
.WithName("AddTestData")
.WithTags("Testing")
.WithOpenApi();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

