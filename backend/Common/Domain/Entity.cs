namespace MC.Erp.Common.Domain;

/// <summary>
/// 實體基類，所有實體繼承此類。
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public override bool Equals(object? obj)
        => obj is Entity other && other.Id == Id;

    public override int GetHashCode()
        => Id.GetHashCode();

    public static bool operator ==(Entity? a, Entity? b)
        => a?.Equals(b) ?? b is null;

    public static bool operator !=(Entity? a, Entity? b)
        => !(a == b);
}
