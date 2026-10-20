using FluentValidation;

namespace Platform.Application.Identity;

/// <summary>One definition of an acceptable password, shared by register, change and reset.</summary>
public static class PasswordRules
{
    public const int MinimumLength = 10;

    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(MinimumLength).MaximumLength(200)
            .WithMessage($"Password must be at least {MinimumLength} characters.");
}
