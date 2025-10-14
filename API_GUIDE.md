# 🚀 CacheManager API - Swagger & CRUD Guide

## 📋 Tổng quan

Dự án đã được bổ sung:
- ✅ **Swagger UI** - Giao diện test API
- ✅ **CRUD APIs** - Đầy đủ Create, Read, Update, Delete cho Map
- ✅ **OpenAPI Documentation** - Tài liệu API tự động

## 🔧 Setup

### Packages đã thêm:
```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.0" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.9.0" />
```

### Swagger Configuration:
```csharp
// Program.cs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CacheManager API", Version = "v1" });
});

// Enable Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "CacheManager API v1");
});
```

## 🌐 Truy cập Swagger UI

### URL:
```
http://localhost:5011/swagger
```

### Giao diện Swagger sẽ hiển thị:
- **Map CRUD** - 5 endpoints
- **Testing** - 1 endpoint để thêm dữ liệu test

## 📡 CRUD API Endpoints

### 1. **GET** - Lấy giá trị theo key
```
GET /api/map/{mapName}/{key}
```

**Parameters:**
- `mapName` (string): Tên map (vd: "user-sessions")
- `key` (string): Key cần lấy

**Response 200:**
```json
{
  "key": "user1",
  "value": "session-token-abc123"
}
```

**Response 404:**
```json
{
  "error": "Key 'user999' not found in map 'user-sessions'"
}
```

**Example:**
```bash
curl -X GET "http://localhost:5011/api/map/user-sessions/user1"
```

---

### 2. **POST** - Tạo mới hoặc cập nhật giá trị
```
POST /api/map/{mapName}/{key}?value={value}
```

**Parameters:**
- `mapName` (string): Tên map
- `key` (string): Key để set
- `value` (string, query): Giá trị mới

**Response 200:**
```json
{
  "message": "Value set successfully",
  "key": "user1",
  "value": "new-session-token"
}
```

**Example:**
```bash
curl -X POST "http://localhost:5011/api/map/user-sessions/user1?value=new-token-123"
```

---

### 3. **PUT** - Cập nhật giá trị (giống POST)
```
PUT /api/map/{mapName}/{key}?value={value}
```

**Parameters:**
- `mapName` (string): Tên map
- `key` (string): Key cần update
- `value` (string, query): Giá trị mới

**Response 200:**
```json
{
  "message": "Value updated successfully",
  "key": "user1",
  "value": "updated-token"
}
```

**Example:**
```bash
curl -X PUT "http://localhost:5011/api/map/user-sessions/user1?value=updated-token-456"
```

---

### 4. **DELETE** - Xóa toàn bộ dữ liệu trong map
```
DELETE /api/map/{mapName}
```

**Parameters:**
- `mapName` (string): Tên map cần xóa

**Response 200:**
```json
{
  "message": "Map 'user-sessions' cleared successfully"
}
```

**⚠️ Cảnh báo:** Xóa TẤT CẢ dữ liệu trong map!

**Example:**
```bash
curl -X DELETE "http://localhost:5011/api/map/user-sessions"
```

---

### 5. **GET** - Lấy tất cả giá trị (có phân trang)
```
GET /api/map/{mapName}?page={page}&pageSize={pageSize}
```

**Parameters:**
- `mapName` (string): Tên map
- `page` (int, optional): Số trang (mặc định = 1)
- `pageSize` (int, optional): Số records/trang (mặc định = 20)

**Response 200:**
```json
{
  "mapName": "user-sessions",
  "entries": [
    {
      "key": "user1",
      "value": "session-token-abc",
      "version": "a1b2c3d4"
    },
    {
      "key": "user2",
      "value": "session-token-def",
      "version": "e5f6g7h8"
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 50,
    "totalPages": 3
  }
}
```

**Example:**
```bash
# Page 1
curl -X GET "http://localhost:5011/api/map/user-sessions?page=1&pageSize=20"

# Page 2
curl -X GET "http://localhost:5011/api/map/user-sessions?page=2&pageSize=20"
```

---

### 6. **GET** - Thêm dữ liệu test
```
GET /test/add-data
```

**Response 200:**
```json
{
  "success": true,
  "message": "50 user-sessions and 30 user-data records added!"
}
```

**Example:**
```bash
curl -X GET "http://localhost:5011/test/add-data"
```

## 🧪 Test CRUD với Swagger UI

### Bước 1: Khởi động ứng dụng
```powershell
.\run_aspnet.cmd
```

### Bước 2: Mở Swagger UI
```
http://localhost:5011/swagger
```

### Bước 3: Thêm dữ liệu test
1. Tìm endpoint: `GET /test/add-data`
2. Click **"Try it out"**
3. Click **"Execute"**
4. Xem response: 50 records added

### Bước 4: Test READ (GET)
1. Tìm endpoint: `GET /api/map/{mapName}/{key}`
2. Click **"Try it out"**
3. Nhập:
   - `mapName`: `user-sessions`
   - `key`: `user1`
4. Click **"Execute"**
5. Xem response với key-value

### Bước 5: Test CREATE/UPDATE (POST)
1. Tìm endpoint: `POST /api/map/{mapName}/{key}`
2. Click **"Try it out"**
3. Nhập:
   - `mapName`: `user-sessions`
   - `key`: `newuser`
   - `value`: `my-new-token-123`
4. Click **"Execute"**
5. Xem response: "Value set successfully"

### Bước 6: Test UPDATE (PUT)
1. Tìm endpoint: `PUT /api/map/{mapName}/{key}`
2. Click **"Try it out"**
3. Nhập:
   - `mapName`: `user-sessions`
   - `key`: `user1`
   - `value`: `updated-token-999`
4. Click **"Execute"**
5. Xem response: "Value updated successfully"

### Bước 7: Test READ ALL (GET với pagination)
1. Tìm endpoint: `GET /api/map/{mapName}`
2. Click **"Try it out"**
3. Nhập:
   - `mapName`: `user-sessions`
   - `page`: `1`
   - `pageSize`: `10`
4. Click **"Execute"**
5. Xem response: 10 records đầu tiên + pagination info

### Bước 8: Test DELETE
1. Tìm endpoint: `DELETE /api/map/{mapName}`
2. Click **"Try it out"**
3. Nhập:
   - `mapName`: `user-sessions`
4. Click **"Execute"**
5. Xem response: "Map cleared successfully"
6. Test lại GET để verify: map đã rỗng

## 🎯 Use Cases

### Use Case 1: Thêm user session
```bash
# POST new session
curl -X POST "http://localhost:5011/api/map/user-sessions/john?value=session-abc-123"

# GET to verify
curl -X GET "http://localhost:5011/api/map/user-sessions/john"
```

### Use Case 2: Cập nhật session
```bash
# PUT update session
curl -X PUT "http://localhost:5011/api/map/user-sessions/john?value=session-xyz-789"

# GET to verify
curl -X GET "http://localhost:5011/api/map/user-sessions/john"
```

### Use Case 3: Xem tất cả sessions
```bash
# GET all with pagination
curl -X GET "http://localhost:5011/api/map/user-sessions?page=1&pageSize=20"
```

### Use Case 4: Xóa tất cả sessions (clean up)
```bash
# DELETE all
curl -X DELETE "http://localhost:5011/api/map/user-sessions"

# Verify empty
curl -X GET "http://localhost:5011/api/map/user-sessions"
```

## 📊 Kiểm tra trên Dashboard

Sau khi thực hiện CRUD operations, bạn có thể xem kết quả trên dashboard:

```
http://localhost:5011/cache-manager
```

1. Click tab **"Map"**
2. Chọn **"user-sessions"** từ nav bên trái
3. Thấy dữ liệu đã thay đổi theo CRUD operations
4. Test search và pagination

## 🔒 Lưu ý quan trọng

### Map Type Constraints
- Hiện tại API chỉ support `Map<string, string>`
- Nếu muốn dùng `Map<int, string>`, cần tạo API riêng
- Có thể extend bằng generic endpoints

### Error Handling
- **404**: Key không tồn tại
- **500**: Redis connection issues
- **400**: Invalid parameters

### Redis Connection
- Đảm bảo Redis đang chạy trên `localhost:6379`
- Kiểm tra connection string trong `Program.cs`

## 📚 OpenAPI Specification

Swagger tự động generate OpenAPI spec tại:
```
http://localhost:5011/swagger/v1/swagger.json
```

Có thể import vào:
- Postman
- Insomnia
- API testing tools khác

## 🎉 Kết luận

Bạn đã có:
- ✅ Swagger UI đầy đủ
- ✅ 5 CRUD endpoints cho Map
- ✅ Pagination support
- ✅ Test data endpoint
- ✅ OpenAPI documentation
- ✅ Integration với Dashboard

**Happy Testing!** 🚀
