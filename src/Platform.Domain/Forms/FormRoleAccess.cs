namespace Platform.Domain.Forms;

/// <summary>
/// Composite key of two already-existing GUIDs (FormDefinitionId + RoleId) - the exact
/// same shape as UserRole, which means the exact same EF tracking gotcha applies here too.
/// See FormDefinition.GrantAccess for the fix.
/// </summary>
public class FormRoleAccess
{
    public Guid FormDefinitionId { get; private set; }
    public Guid RoleId { get; private set; }

    private FormRoleAccess() { }

    public static FormRoleAccess Create(Guid formDefinitionId, Guid roleId) => new()
    {
        FormDefinitionId = formDefinitionId,
        RoleId = roleId
    };
}
