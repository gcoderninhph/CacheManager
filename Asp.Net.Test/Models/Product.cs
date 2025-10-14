namespace Asp.Net.Test.Models;

/// <summary>
/// Model để test tính năng Batch Update
/// System sẽ tự động cập nhật 5 products ngẫu nhiên mỗi phút
/// Batch update sẽ gom tất cả thay đổi trong 3 giây
/// </summary>
public class Product
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateTime LastUpdated { get; set; }
    public int UpdateCount { get; set; }
}
