namespace MC.Erp.Common.Domain;

/// <summary>
/// 帶審計字段的實體基類。
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
