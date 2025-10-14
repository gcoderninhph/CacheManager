# UserInfo CRUD API Guide

## 📋 Overview

Complete CRUD API for managing `UserInfo` objects in CacheManager. The `user-info` map stores user information with typed objects instead of strings.

## 🎯 UserInfo Model

```csharp
namespace Asp.Net.Test.Models;

public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; } = 0;
}
```

## 🔧 Map Registration

**Services/CacheRegistrationBackgroundService.cs:**

```csharp
protected override void ConfigureCache(IRegisterBuilder builder)
{
    // Register UserInfo map: Key=string (userId), Value=UserInfo object
    builder.CreateMap<string, UserInfo>("user-info");
}
```

## 🚀 API Endpoints

### Base URL
```
http://localhost:5011/api/userinfo
```

### 1. Get User by UserId

**Endpoint:** `GET /api/userinfo/{userId}`

**Description:** Retrieve a specific user by their userId.

**Request:**
```http
GET /api/userinfo/user-001
```

**Response (200 OK):**
```json
{
  "userId": "user-001",
  "name": "Alice1",
  "age": 21
}
```

**Response (404 Not Found):**
```json
{
  "error": "User 'user-999' not found"
}
```

**Swagger Test:**
```
GET /api/userinfo/user-001
```

---

### 2. Create or Update User

**Endpoint:** `POST /api/userinfo`

**Description:** Create a new user or update if userId already exists.

**Request Body:**
```json
{
  "userId": "user-100",
  "name": "John Doe",
  "age": 30
}
```

**Response (200 OK):**
```json
{
  "message": "User created/updated successfully",
  "userInfo": {
    "userId": "user-100",
    "name": "John Doe",
    "age": 30
  }
}
```

**Response (400 Bad Request):**
```json
{
  "error": "UserId is required"
}
```

**cURL Example:**
```bash
curl -X POST http://localhost:5011/api/userinfo \
  -H "Content-Type: application/json" \
  -d '{"userId":"user-100","name":"John Doe","age":30}'
```

---

### 3. Update User

**Endpoint:** `PUT /api/userinfo/{userId}`

**Description:** Update an existing user. Returns 404 if user doesn't exist.

**Request:**
```http
PUT /api/userinfo/user-001
Content-Type: application/json

{
  "userId": "user-001",
  "name": "Alice Updated",
  "age": 25
}
```

**Response (200 OK):**
```json
{
  "message": "User updated successfully",
  "userInfo": {
    "userId": "user-001",
    "name": "Alice Updated",
    "age": 25
  }
}
```

**Response (404 Not Found):**
```json
{
  "error": "User 'user-999' not found"
}
```

**Note:** The `userId` in the route takes precedence over the body.

---

### 4. Delete User (Individual)

**Endpoint:** `DELETE /api/userinfo/{userId}`

**Description:** ⚠️ Currently not implemented - IMap interface doesn't support individual item deletion.

**Response (501 Not Implemented):**
```json
{
  "error": "Individual user deletion not supported",
  "message": "Use DELETE /api/userinfo to clear all users, or implement custom Redis delete"
}
```

**Workaround:** To "delete" a user, you can:
1. Use `DELETE /api/userinfo` to clear all users
2. Implement custom Redis HDEL command
3. Set user to null/inactive state

---

### 5. Get All Users (Paginated)

**Endpoint:** `GET /api/userinfo`

**Description:** Retrieve all users with server-side pagination using optimized HSCAN.

**Query Parameters:**
- `page` (int, default=1): Page number
- `pageSize` (int, default=20): Users per page

**Request:**
```http
GET /api/userinfo?page=1&pageSize=20
```

**Response:**
```json
{
  "mapName": "user-info",
  "data": {
    "entries": [
      {
        "key": "user-001",
        "value": "{\"UserId\":\"user-001\",\"Name\":\"Alice1\",\"Age\":21}",
        "version": "a1b2c3d4"
      },
      {
        "key": "user-002",
        "value": "{\"UserId\":\"user-002\",\"Name\":\"Bob2\",\"Age\":22}",
        "version": "e5f67890"
      }
    ],
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 25,
    "totalPages": 2,
    "hasNext": true,
    "hasPrev": false
  }
}
```

**Performance:** Uses Redis HSCAN for efficient pagination (see [OPTIMIZATION_GUIDE.md](OPTIMIZATION_GUIDE.md))

---

### 6. Clear All Users

**Endpoint:** `DELETE /api/userinfo`

**Description:** Delete all users from the map.

**Request:**
```http
DELETE /api/userinfo
```

**Response (200 OK):**
```json
{
  "message": "All users cleared successfully"
}
```

**⚠️ Warning:** This operation cannot be undone!

---

## 🧪 Testing

### 1. Add Test Data

**Endpoint:** `GET /test/add-data`

Adds 25 sample UserInfo records for testing:

```http
GET http://localhost:5011/test/add-data
```

**Response:**
```json
{
  "success": true,
  "message": "Test data added: 50 user-sessions, 30 user-data, 25 user-info!"
}
```

**Generated Users:**
- user-001 to user-025
- Names: Alice1-25, Bob1-25, Charlie1-25, etc.
- Ages: 21-70 (cycling pattern)

### 2. Test via Swagger UI

1. Open Swagger: `http://localhost:5011/swagger`
2. Find **UserInfo CRUD** section
3. Click on any endpoint to expand
4. Click **Try it out**
5. Fill in parameters/body
6. Click **Execute**

### 3. Test via Dashboard

1. Open Dashboard: `http://localhost:5011/cache-manager`
2. Click on **Map** tab
3. Select **user-info** from left navigation
4. View paginated users in the table
5. Use search box to filter by userId

---

## 📊 Complete Example Flow

### Scenario: Create, Read, Update, Delete User

```bash
# 1. Create a new user
curl -X POST http://localhost:5011/api/userinfo \
  -H "Content-Type: application/json" \
  -d '{"userId":"john-123","name":"John Smith","age":28}'

# 2. Get the user
curl http://localhost:5011/api/userinfo/john-123

# 3. Update the user
curl -X PUT http://localhost:5011/api/userinfo/john-123 \
  -H "Content-Type: application/json" \
  -d '{"userId":"john-123","name":"John Smith Updated","age":29}'

# 4. Get all users (page 1)
curl http://localhost:5011/api/userinfo?page=1&pageSize=10

# 5. Delete individual user (not supported)
curl -X DELETE http://localhost:5011/api/userinfo/john-123
# Returns 501 Not Implemented

# 6. Clear all users
curl -X DELETE http://localhost:5011/api/userinfo
```

---

## 💡 Code Usage in Application

### Inject and Use Map

```csharp
public class UserService
{
    private readonly ICacheStorage _storage;

    public UserService(ICacheStorage storage)
    {
        _storage = storage;
    }

    public async Task<UserInfo> GetUserAsync(string userId)
    {
        var map = _storage.GetMap<string, UserInfo>("user-info");
        return await map.GetValueAsync(userId);
    }

    public async Task SaveUserAsync(UserInfo user)
    {
        var map = _storage.GetMap<string, UserInfo>("user-info");
        await map.SetValueAsync(user.UserId, user);
    }
}
```

### With Event Handlers

```csharp
public void ConfigureUserEvents(ICacheStorage storage)
{
    var map = storage.GetMap<string, UserInfo>("user-info");

    // Log when user is added
    map.OnAdd((userId, user) =>
    {
        Console.WriteLine($"User added: {userId} - {user.Name}");
    });

    // Log when user is updated
    map.OnUpdate((userId, user) =>
    {
        Console.WriteLine($"User updated: {userId} - {user.Name}, Age: {user.Age}");
    });

    // Batch update to database
    map.OnBatchUpdate(async (entries) =>
    {
        var users = entries.Select(e => e.GetValue()).ToList();
        await _database.BulkUpdateUsersAsync(users);
        Console.WriteLine($"Batch updated {users.Count} users to database");
    });
}
```

---

## 🔒 Security Considerations

### 1. Validation

Add FluentValidation or Data Annotations:

```csharp
public class UserInfo
{
    [Required]
    [StringLength(50)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 150)]
    public int Age { get; set; } = 0;
}
```

### 2. Authorization

Add authorization to endpoints:

```csharp
app.MapPost("/api/userinfo", async (UserInfo userInfo, ICacheStorage storage) =>
{
    // ... implementation
})
.RequireAuthorization("AdminPolicy"); // Add auth
```

### 3. Rate Limiting

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("userapi", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);
        opt.PermitLimit = 100;
    });
});

app.MapPost("/api/userinfo", ...)
   .RequireRateLimiting("userapi");
```

---

## 🎯 Best Practices

1. ✅ **Always validate input** - Check UserId is not empty
2. ✅ **Handle KeyNotFoundException** - Return 404 for missing users
3. ✅ **Use pagination** - Don't load all users at once
4. ✅ **Implement proper error handling** - Return meaningful error messages
5. ✅ **Use DTOs** - Separate API models from domain models if needed
6. ✅ **Add logging** - Log user operations for audit trail
7. ✅ **Version your API** - Use /api/v1/userinfo for versioning
8. ✅ **Document with Swagger** - Keep API documentation up to date

---

## 📚 Related Documentation

- [CONFIGURATION_GUIDE.md](CONFIGURATION_GUIDE.md) - Configuration setup
- [API_GUIDE.md](API_GUIDE.md) - General CRUD API reference
- [OPTIMIZATION_GUIDE.md](OPTIMIZATION_GUIDE.md) - Performance optimization
- [README.md](README.md) - Project overview

---

## 🔄 Future Enhancements

- [ ] Add individual delete support (implement custom Redis HDEL)
- [ ] Add bulk create/update endpoint
- [ ] Add search by name/age
- [ ] Add filtering (e.g., age range)
- [ ] Add sorting options
- [ ] Add export to CSV/JSON
- [ ] Add import from file
- [ ] Add user avatars (store URL)
- [ ] Add audit trail (who/when modified)
- [ ] Add soft delete (IsDeleted flag)

---

Created: 2024
Author: CacheManager Team
