namespace StansfeldScott.Portal.Domain.Entities;

/// <summary>
/// Base class for all persisted entities. Add module-specific entities
/// (Distributor, Order, Invoice, Document, Contact, etc.) here once the
/// corresponding CRM API contract is confirmed.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
