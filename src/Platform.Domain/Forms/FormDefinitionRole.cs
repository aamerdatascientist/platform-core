namespace Platform.Domain.Forms;

/// <summary>
/// A form with zero rows here is visible to everyone - deliberate, not a bug. Restricting
/// a form is something an admin opts into per form, not the default state, since this
/// feature ships into a database with 21 real forms already in use.
/// </summary>
public class FormDefinitionRole
{
    public Guid FormDefinitionId { get; private set; }
    public Guid RoleId { get; private set; }

    private FormDefinitionRole() { }

    public static FormDefinitionRole Create(Guid formDefinitionId, Guid roleId) => new()
    {
        FormDefinitionId = formDefinitionId,
        RoleId = roleId
    };
}
