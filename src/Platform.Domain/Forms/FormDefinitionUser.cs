namespace Platform.Domain.Forms;

/// <summary>
/// A second, independent layer of access alongside FormDefinitionRole - a user gets
/// access if their role is allowed, OR they're granted access directly here, OR neither
/// list has anything in it at all (still open to everyone by default - see
/// FormAccessChecker). This exists for the case role-based access doesn't fit well: two
/// people who'd otherwise share a role but genuinely need different form access, without
/// forcing a new role to be invented for every such case.
/// </summary>
public class FormDefinitionUser
{
    public Guid FormDefinitionId { get; private set; }
    public Guid UserId { get; private set; }

    private FormDefinitionUser() { }

    public static FormDefinitionUser Create(Guid formDefinitionId, Guid userId) => new()
    {
        FormDefinitionId = formDefinitionId,
        UserId = userId
    };
}
