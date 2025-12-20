using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CacheManager.Core;

public interface IMap<TKey, TValue>
{
    /// <summary>
    /// Lấy giá trị từ map theo key, trả về null nếu key không tồn tại
    /// </summary>
    Task<TValue?> GetValueAsync(TKey key);
    /// <summary>
    /// Set giá trị cho key, nếu key đã tồn tại thì cập nhật giá trị mới
    /// </summary>
    Task SetValueAsync(TKey key, TValue value);
    /// <summary>
    /// Chạy callback khi có sự kiện thêm
    /// </summary>
    void OnAdd(Func<TKey, TValue, Task> addAction);
    /// <summary>
    /// Chạy callback khi có sự kiện cập nhật
    /// </summary>
    void OnUpdate(Func<TKey, TValue, Task> updateAction);
    /// <summary>
    /// Chạy callback khi có sự kiện xóa
    /// </summary>
    void OnRemove(Func<TKey, TValue, Task> removeAction);
    /// <summary>
    /// Chạy callback khi có sự kiện xóa tất cả
    /// </summary>
    void OnClear(Func<Task> clearAction);

    /// <summary>
    /// Gom tất cả những (key/value) có thay đổi trong một khoảng thời gian nhất định rồi gọi callback một lần, call back này mục đích để bên asp.net cập nhật db
    /// Logic: Mỗi khi có sự kiện cập nhật (Check bằng thay đổi version trong map), mỗi key sẽ có version và time cập nhật, 
    /// nếu trong  khoảng thời gian không còn cập nhật nữa thì gom lại vào batch và gọi callback
    /// thời gian chờ được config trong appsetting.json của asp.net
    /// thời gian check gom batch là 1s
    /// </summary>
    void OnBatchUpdate(Func<IEnumerable<IEntry<TKey, TValue>>, Task> batchUpdateAction);

    /// <summary>
    /// Cấu hình TTL (Time To Live) cho từng phần tử trong map
    /// Nếu một phần tử không có bất kỳ hoạt động nào (Get/Set) trong khoảng thời gian này, nó sẽ tự động bị xóa
    /// Logic: Dùng Redis Sorted Set phụ để tracking thời gian truy cập cuối cùng
    /// Background timer sẽ check và xóa các keys hết hạn mỗi giây
    /// </summary>
    /// <param name="ttl">Thời gian hết hạn. Nếu null thì tắt TTL</param>
    void SetItemExpiration(TimeSpan? ttl);

    /// <summary>
    /// Callback khi một phần tử hết hạn và bị xóa tự động
    /// </summary>
    void OnExpired(Func<TKey, TValue, Task> expiredAction);
    /// <summary>
    /// Kiểm tra key có tồn tại trong map không
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    Task<bool> ContainsKeyAsync(TKey key);
    /// <summary>
    /// Số phần tử trong map
    /// </summary>
    /// <returns></returns>
    Task<int> CountAsync();

    /// <summary>
    /// Lấy tất cả keys, values, entries trong map
    /// </summary>
    Task<IEnumerable<TValue>> GetAllValuesAsync();
    /// <summary>
    /// Lấy tất cả keys, values, entries trong map
    /// </summary>
    /// <returns></returns>
    Task<IEnumerable<TKey>> GetAllKeysAsync();
    /// <summary>
    /// Lấy tất cả keys, values, entries trong map
    /// </summary>
    /// <returns></returns>
    Task<IEnumerable<IEntry<TKey, TValue>>> GetAllEntriesAsync();

    /// <summary>
    /// Duyệt tất cả keys trong map, tôi ưu cho trường hợp map có rất nhiều phần tử
    /// </summary>
    /// <param name="keyAction"></param>
    /// <returns></returns>
    Task GetAllKeysAsync(Action<TKey> keyAction);
    /// <summary>
    /// Duyệt tất cả values trong map, tôi ưu cho trường hợp map có rất nhiều phần tử
    /// </summary>
    /// <param name="valueAction"></param>
    /// <returns></returns>
    Task GetAllValuesAsync(Action<TValue> valueAction);
    /// <summary>
    /// Duyệt tất cả entries trong map, tôi ưu cho trường hợp map có rất nhiều phần tử
    /// </summary>
    Task GetAllEntriesAsync(Action<IEntry<TKey, TValue>> entryAction);
    /// <summary>
    /// Xóa một phần tử trong map
    /// </summary>
    Task<bool> RemoveAsync(TKey key);
    /// <summary>
    /// Xóa tất cả phần tử trong map
    /// </summary>
    Task ClearAsync();
}

public interface IEntry<TKey, TValue>
{
    TKey GetKey();
    TValue GetValue();
}