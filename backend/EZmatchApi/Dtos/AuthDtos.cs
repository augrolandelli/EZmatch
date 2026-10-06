using EZmatchApi.Models;
using FluentValidation;

namespace EZmatchApi.Dtos;

/// <summary>Datos públicos del usuario del panel — nunca el hash.</summary>
public record UserDto(Guid Id, string Email, string FullName, UserRole Role, Guid? ClubId, string? ClubName);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Par de tokens + usuario autenticado.</summary>
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Ingresá tu email.").EmailAddress().WithMessage("El email no es válido.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Ingresá tu contraseña.");
    }
}

public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Ingresá tu contraseña actual.");
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}

public static class PasswordRules
{
    /// <summary>Mínimo 8 caracteres, máximo 100.</summary>
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Ingresá una contraseña.")
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .MaximumLength(100).WithMessage("La contraseña es demasiado larga.");
}
