
using FluentValidation;
using MsFundamentals.Trainer.DTOs;

namespace MsFundamentals.Trainer.Validation;

public sealed class AnalysisRequestValidator : AbstractValidator<AnalysisRequest>
{
    public AnalysisRequestValidator()
    {
        RuleFor(x => x.AnalysisMode).Must(m => m is "light" or "deep").WithMessage("AnalysisMode deve ser 'light' ou 'deep'");
        RuleFor(x => x.Language).NotEmpty();
    }
}
