using EZmatchApi.Models;
using FluentValidation;

namespace EZmatchApi.Dtos;

/// <param name="IsMe">Es el usuario que está haciendo la consulta (el panel no le deja desactivarse).</param>
public record ClubUserDto(
    Guid Id,
    string Email,
    string FullName,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool IsMe);

/// <summary>Alta de un usuario del club. La contraseña inicial la define el dueño (no hay envío de mails).</summary>
public record CreateClubUserRequest(string Email, string FullName, UserRole Role, string Password);

public record UpdateClubUserRequest(string FullName, UserRole Role, bool IsActive);

public record ResetPasswordRequest(string Password);

public class CreateClubUserRequestValidator : AbstractValidator<CreateClubUserRequest>
{
    public CreateClubUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Ingresá el email.").EmailAddress().WithMessage("El email no es válido.").MaximumLength(254);
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Ingresá el nombre.").MaximumLength(120);
        RuleFor(x => x.Role).Must(r => r is UserRole.Owner or UserRole.Staff).WithMessage("El rol tiene que ser Dueño o Recepción.");
        RuleFor(x => x.Password).ValidPassword();
    }
}

public class UpdateClubUserRequestValidator : AbstractValidator<UpdateClubUserRequest>
{
    public UpdateClubUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Ingresá el nombre.").MaximumLength(120);
        RuleFor(x => x.Role).Must(r => r is UserRole.Owner or UserRole.Staff).WithMessage("El rol tiene que ser Dueño o Recepción.");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator() => RuleFor(x => x.Password).ValidPassword();
}
