# 📄 Pagination & Search - Hướng dẫn sử dụng

## ✨ Tính năng đã bổ sung

### 1. **Phân trang (Pagination)**
- ✅ Giới hạn **20 bản ghi / trang**
- ✅ Nút **Previous** và **Next** để chuyển trang
- ✅ Hiển thị thông tin: "Page X of Y"
- ✅ Tự động disable nút khi ở trang đầu/cuối

### 2. **Tìm kiếm (Search)**
- ✅ Search box để tìm theo **key**
- ✅ Tìm kiếm không phân biệt hoa thường (case-insensitive)
- ✅ Debounce 300ms (chờ người dùng ngừng gõ)
- ✅ Tự động reset về trang 1 khi search

### 3. **API đã cập nhật**

#### Endpoint: `GET /cache-manager/api/map/{mapName}`

**Query Parameters:**
```
page      : int    (mặc định = 1)
pageSize  : int    (mặc định = 20)
search    : string (optional, tìm theo key)
```

**Response:**
```json
{
  "mapName": "user-sessions",
  "entries": [
    {
      "key": "user1",
      "value": "session-token-abc",
      "version": "a1b2c3d4"
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 50,
    "totalPages": 3,
    "hasNext": true,
    "hasPrev": false
  }
}
```

## 🎯 Demo sử dụng

### 1. Khởi động ứng dụng
```bash
.\run_aspnet.cmd
```

### 2. Thêm dữ liệu test (> 20 records)
Truy cập: `http://localhost:5011/test/add-data`

Sẽ thêm:
- **50 records** vào map `user-sessions`
- **30 records** vào map `user-data`

### 3. Xem Dashboard với Pagination
Truy cập: `http://localhost:5011/cache-manager`

#### Test Pagination:
1. Click vào map `user-sessions` (50 records)
2. Thấy hiển thị: "Page 1 of 3" (50 ÷ 20 = 3 pages)
3. Chỉ hiển thị 20 records đầu tiên
4. Click **Next** để xem trang 2 (records 21-40)
5. Click **Next** lần nữa để xem trang 3 (records 41-50)
6. Nút **Next** sẽ bị disable ở trang cuối

#### Test Search:
1. Gõ "user1" vào search box
2. Chờ 300ms (debounce)
3. Kết quả: chỉ hiển thị records có key chứa "user1"
   - user1, user10, user11, user12, ..., user19
4. Pagination sẽ cập nhật dựa trên kết quả search

### 4. Ví dụ API Calls

#### Lấy trang 1:
```
GET /cache-manager/api/map/user-sessions?page=1&pageSize=20
```

#### Lấy trang 2:
```
GET /cache-manager/api/map/user-sessions?page=2&pageSize=20
```

#### Tìm kiếm "user1":
```
GET /cache-manager/api/map/user-sessions?page=1&pageSize=20&search=user1
```

## 🎨 UI Components

### Search Box
```html
<input type="text" id="search-box" placeholder="Search by key..." />
```

**CSS:**
- Rounded corners (50px)
- Dark background với blue border
- Focus effect: glow border
- Min width: 250px

### Pagination Controls
```html
<div class="pagination">
    <button id="prev-page">← Previous</button>
    <span id="page-info">Page 1 of 3</span>
    <button id="next-page">Next →</button>
</div>
```

**CSS:**
- Centered layout với gap: 2rem
- Gradient hover effect
- Disabled state: opacity 0.3
- Rounded buttons (50px)

## 💡 Chi tiết kỹ thuật

### Debounce Search
```javascript
searchBox.addEventListener('input', (e) => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
        // Fetch data after 300ms
        fetchMapData(currentMapName, 1, searchValue);
    }, 300);
});
```

### Pagination Logic (Backend)
```csharp
// Filter by search
if (!string.IsNullOrWhiteSpace(search))
{
    entriesList = entriesList.Where(e =>
        e.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
    ).ToList();
}

// Calculate total pages
var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

// Apply pagination
var pagedEntries = entriesList
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToList();
```

## 📊 Performance

- **Page size**: 20 records (optimal cho UX)
- **Search debounce**: 300ms (giảm API calls)
- **Pagination server-side**: giảm tải network
- **Case-insensitive search**: dễ sử dụng hơn

## 🔧 Tùy chỉnh

### Thay đổi page size
Trong `app.js`:
```javascript
const params = new URLSearchParams({
    page: page.toString(),
    pageSize: '50'  // Thay đổi từ 20 → 50
});
```

### Thay đổi debounce time
Trong `app.js`:
```javascript
setTimeout(() => {
    fetchMapData(currentMapName, 1, searchValue);
}, 500);  // Thay đổi từ 300ms → 500ms
```

## ✅ Checklist hoàn thành

- ✅ Giới hạn 20 bản ghi / trang
- ✅ Nút Previous / Next
- ✅ Hiển thị Page X of Y
- ✅ Search theo key
- ✅ Case-insensitive search
- ✅ Debounce search input
- ✅ Server-side pagination
- ✅ Responsive UI
- ✅ Disable buttons khi cần
- ✅ Reset về page 1 khi search

Enjoy! 🎉
