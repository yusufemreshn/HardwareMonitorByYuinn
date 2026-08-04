namespace HardwareMonitorByYuinn.Entity.History;

/// <summary>
/// Yerel ağa açma PIN kapısına yapılan tek bir giriş denemesi. Girilen PIN'in kendisi hiçbir zaman
/// saklanmaz — yalnızca kimin (IP), ne zaman ve sonucun ne olduğu (başarılı/başarısız, kilide yol
/// açıp açmadığı).
/// </summary>
public sealed class LoginAttemptEntry
{
    public required DateTime TimestampUtc { get; init; }
    public required string IpAddress { get; init; }
    public required bool Success { get; init; }
    public required bool CausedLockout { get; init; }
}
