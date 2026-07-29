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
}
