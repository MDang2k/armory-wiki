using System.Text.Json;
using ArmoryWiki.Domain;
using FluentValidation;

namespace ArmoryWiki.Api.Common;

public static class ValidationExtensions
{
    /// <summary>Error keys use the JSON field names ("designDate.month"), like the rest of the API.</summary>
    public static void ConfigureValidation()
    {
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
            member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);
        // Stop a rule at its first failure, so NotNull() guards the checks chained after it.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
    }

    /// <summary>Runs the registered validator for the <typeparamref name="T"/> body; invalid bodies get a 400 ValidationProblem.</summary>
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            if (context.Arguments.OfType<T>().FirstOrDefault() is { } body)
            {
                var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
                var result = await validator.ValidateAsync(body, context.HttpContext.RequestAborted);
                if (!result.IsValid)
                {
                    return TypedResults.ValidationProblem(result.ToDictionary());
                }
            }

            return await next(context);
        }).ProducesValidationProblem();
}

public sealed class FuzzyDateValidator : AbstractValidator<FuzzyDateDto>
{
    public FuzzyDateValidator()
    {
        RuleFor(d => d.Year).InclusiveBetween(1, 2100);
        RuleFor(d => d.Month).InclusiveBetween(1, 12).When(d => d.Month is not null);
        RuleFor(d => d.Day)
            .Must((d, day) => d.Month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(d.Year, d.Month.Value))
            .When(d => d.Day is not null && d.Year is >= 1 and <= 2100)
            .WithMessage("Day must be a valid day of the given month, and needs a month.");
        RuleFor(d => d.YearTo).GreaterThanOrEqualTo(d => d.Year).When(d => d.YearTo is not null);
        RuleFor(d => d.Precision)
            .Must((d, precision) => precision switch
            {
                DatePrecision.Day => d.Month is not null && d.Day is not null,
                DatePrecision.Month => d.Month is not null && d.Day is null,
                DatePrecision.Year => d.Month is null && d.Day is null,
                DatePrecision.Decade => d.Month is null && d.Day is null && d.Year % 10 == 0 && d.YearTo == d.Year + 9,
                _ => false,
            })
            .WithMessage("Precision must match the parts given: Day needs month and day, Month needs a month only, "
                + "Year has neither, and a Decade is a range like 1950–1959.");
    }
}
