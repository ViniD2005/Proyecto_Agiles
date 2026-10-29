using EduMonitor.Identity.Api.Contracts;
using FluentValidation;

namespace EduMonitor.Identity.Api.Services;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("El correo es obligatorio.")
                             .EmailAddress().WithMessage("El correo no tiene un formato válido.")
                             .MaximumLength(150);
        RuleFor(x => x.Password).NotEmpty().WithMessage("La contraseña es obligatoria.")
                                .MaximumLength(100);
    }
}
