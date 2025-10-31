
using FluentValidation;
using MsFundamentals.Trainer.DTOs;

namespace MsFundamentals.Trainer.Validation;

public sealed class CreateExamRequestValidator : AbstractValidator<CreateExamRequest>
{
    public CreateExamRequestValidator()
    {
        RuleFor(x => x.Track).NotEmpty();
        RuleFor(x => x.Count).InclusiveBetween(1, 50);
        RuleFor(x => x.Language).NotEmpty();
        RuleFor(x => x.DifficultyMix).Custom((mix, ctx) =>
        {
            if (mix is null) return;
            var sum = mix.Values.Sum();
            if (sum == 0) { ctx.AddFailure("DifficultyMix", "Soma do mix deve ser > 0"); return; }
        });
    }
}
