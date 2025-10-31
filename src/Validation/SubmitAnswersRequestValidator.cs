
using FluentValidation;
using MsFundamentals.Trainer.DTOs;

namespace MsFundamentals.Trainer.Validation;

public sealed class SubmitAnswersRequestValidator : AbstractValidator<SubmitAnswersRequest>
{
    public SubmitAnswersRequestValidator()
    {
        RuleFor(x => x.Answers).NotEmpty();
        RuleForEach(x => x.Answers).ChildRules(a =>
        {
            a.RuleFor(y => y.QuestionId).NotEmpty();
            a.RuleFor(y => y.Selected).NotEmpty().Must(s => new[] {"A","B","C","D"}.Contains(s))
                .WithMessage("Selected deve ser A, B, C ou D");
        });
    }
}
