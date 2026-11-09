using Platform.Domain.Forms;

namespace Platform.Application.Forms;

public static class FormDefinitionExtensions
{
    /// <summary>
    /// Wraps FormDefinition.GetDraftVersion() so callers get a proper 400 ValidationException
    /// instead of letting the domain's InvalidOperationException reach the generic 500
    /// handler - "no open draft" (e.g. the form is already published) is a normal, expected
    /// outcome for these commands, not a server error.
    /// </summary>
    public static FormVersion GetDraftVersionOrThrow(this FormDefinition formDefinition)
    {
        try
        {
            return formDefinition.GetDraftVersion();
        }
        catch (InvalidOperationException)
        {
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(FormDefinition.Id), "This form has no open draft to modify.")
            });
        }
    }

    /// <summary>
    /// System forms (FormDefinition.IsSystem) are read by calculations by field code: their
    /// fields can be relabelled, but not renamed, retyped, archived or deleted, and the form
    /// can't be deleted (2026-11-10).
    /// </summary>
    public static void ThrowIfSystemForm(this FormDefinition formDefinition, string what) 
    {
        if (formDefinition.IsSystem)
            throw new Common.Exceptions.ValidationException("form.systemLocked",
                $"'{formDefinition.Name}' is a system form that progress figures are calculated from - {what} isn't allowed on it. Labels can still be changed.");
    }

    /// <summary>A field in a repeatable section lives in the section's own table; operations that
    /// rebuild columns through the form's main table don't apply to it.</summary>
    public static void ThrowIfSectionField(this FieldDefinition field, string what)
    {
        if (field.SectionId is not null)
            throw new Common.Exceptions.ValidationException("form.sectionField",
                $"{what} isn't available for a field in a repeatable section. Archive it and add a new field instead.");
    }
}
