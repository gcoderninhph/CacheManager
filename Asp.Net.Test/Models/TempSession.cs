namespace Asp.Net.Test.Models;

/// <summary>
/// Model để test tính năng TTL (Time To Live)
/// Session sẽ tự động hết hạn sau 2 phút không hoạt động
/// </summary>
public class TempSession
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}
