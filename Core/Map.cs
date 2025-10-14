using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CacheManager.Core;

public interface IMap<TKey, TValue>
{
    Task<TValue> GetValueAsync(TKey key);
    Task SetValueAsync(TKey key, TValue value);
    /// <summary>
    /// Chạy callback khi có sự kiện thêm
    /// </summary>
    void OnAdd(Action<TKey, TValue> addAction);
    /// <summary>
    /// Chạy callback khi có sự kiện cập nhật
    /// </summary>
    void OnUpdate(Action<TKey, TValue> updateAction);
    /// <summary>
    /// Chạy callback khi có sự kiện xóa
    /// </summary>
    void OnRemove(Action<TKey, TValue> removeAction);
    /// <summary>
    /// Chạy callback khi có sự kiện xóa tất cả
    /// </summary>
    void OnClear(Action clearAction);

    /// <summary>
    /// Gom tất cả những (key/value) có thay đổi trong một khoảng thời gian nhất định rồi gọi callback một lần, call back này mục đích để bên asp.net cập nhật db
    /// Logic: Mỗi khi có sự kiện cập nhật (Check bằng thay đổi version trong map), mỗi key sẽ có version và time cập nhật, 
    /// nếu trong  khoảng thời gian không còn cập nhật nữa thì gom lại vào batch và gọi callback
    /// thời gian chờ được config trong appsetting.json của asp.net
    /// thời gian check gom batch là 1s
    /// </summary>
    void OnBatchUpdate(Action<IEnumerable<IEntry<TKey, TValue>>> batchUpdateAction);

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
    void OnExpired(Action<TKey, TValue> expiredAction);

    Task ClearAsync();
}

public interface IEntry<TKey, TValue>
{
    TKey GetKey();
    TValue GetValue();
}