using MediatR;

namespace Platform.Domain.Common;

/// <summary>
/// Marker for domain events. Extends MediatR's INotification so the Application layer
/// can dispatch these through the same pipeline as everything else, via a
/// SaveChanges interceptor in Infrastructure - domain events are raised only after
/// a successful commit, never before.
/// </summary>
public interface IDomainEvent : INotification
{
    DateTime OccurredOnUtc { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
