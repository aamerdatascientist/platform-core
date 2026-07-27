namespace Platform.Domain.Common;

/// <summary>
/// Base type for every entity in the static (EF Core-mapped) platform schema.
/// Entities living in dynamically generated per-form tables are NOT modeled here -
/// they are described by FieldDefinition metadata instead, see Platform.Domain.Forms.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Adds created/modified tracking. Every static entity that represents business
/// data (as opposed to pure lookup data) should inherit from this, not BaseEntity directly.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public Guid? ModifiedByUserId { get; set; }
    public bool IsDeleted { get; set; }
}
