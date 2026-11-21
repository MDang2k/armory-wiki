using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using FluentValidation;

namespace ArmoryWiki.Api.Features.Admin.Firearms;

public sealed class FirearmInputValidator : AbstractValidator<FirearmInput>
{
    public FirearmInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug!)
            .Must(Slug.IsValid).WithMessage("Slugs are lowercase letters and digits separated by single hyphens.")
            .Must(s => !Slug.ReservedFirearmSlugs.Contains(s)).WithMessage("This slug is reserved by the API.")
            .When(x => x.Slug is not null);
        RuleForEach(x => x.AlternativeNames).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WikidataId).Matches(@"^Q[1-9][0-9]*\z").When(x => x.WikidataId is not null);
        RuleFor(x => x.Summary).MaximumLength(500);
        RuleFor(x => x.DesignDate!).SetValidator(new FuzzyDateValidator()).When(x => x.DesignDate is not null);
        RuleFor(x => x.ProductionStart!).SetValidator(new FuzzyDateValidator()).When(x => x.ProductionStart is not null);
        RuleFor(x => x.ProductionEnd!).SetValidator(new FuzzyDateValidator()).When(x => x.ProductionEnd is not null);
        RuleFor(x => x.WikipediaRevisionId).GreaterThan(0).When(x => x.WikipediaRevisionId is not null);
    }
}

public sealed class VariantInputValidator : AbstractValidator<VariantInput>
{
    public VariantInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug!).Must(Slug.IsValid).WithMessage("Slugs are lowercase letters and digits separated by single hyphens.")
            .When(x => x.Slug is not null);
        RuleFor(x => x.IntroducedDate!).SetValidator(new FuzzyDateValidator()).When(x => x.IntroducedDate is not null);
        RuleFor(x => x.WeightEmptyG).GreaterThan(0);
        RuleFor(x => x.OverallLengthMm).GreaterThan(0);
        RuleFor(x => x.OverallLengthFoldedMm).GreaterThan(0).LessThanOrEqualTo(x => x.OverallLengthMm)
            .When(x => x.OverallLengthMm is not null);
        RuleFor(x => x.BarrelLengthMm).GreaterThan(0);
        RuleFor(x => x.RateOfFireMinRpm).GreaterThan(0);
        RuleFor(x => x.RateOfFireMaxRpm).GreaterThan(0).GreaterThanOrEqualTo(x => x.RateOfFireMinRpm)
            .When(x => x.RateOfFireMinRpm is not null);
        RuleFor(x => x.MuzzleVelocityMps).GreaterThan(0);
        RuleFor(x => x.EffectiveRangeM).GreaterThan(0);
        RuleFor(x => x.MagazineCapacityStandard).GreaterThan(0);
        RuleFor(x => x.FiringModes).Must(m => m!.Distinct().Count() == m!.Count && !m.Contains(Domain.FiringModes.None))
            .When(x => x.FiringModes is not null)
            .WithMessage("List each firing mode once; leave the list empty for manually operated firearms.");
        RuleForEach(x => x.ExtraSpecs).Must(kv => kv.Key.Length is > 0 and <= 100 && kv.Value.Length <= 500);
    }
}

public sealed class TagsInputValidator : AbstractValidator<TagsInput>
{
    public TagsInputValidator() =>
        RuleFor(x => x.Tags).NotNull().Must(t => t.Distinct().Count() == t.Count).WithMessage("Tags must be unique.");
}

public sealed class DesignersInputValidator : AbstractValidator<DesignersInput>
{
    public DesignersInputValidator()
    {
        RuleFor(x => x.Designers).NotNull();
        RuleForEach(x => x.Designers)
            .Must(d => (d.Person is null) != (d.Organization is null))
            .WithMessage("Each designer names exactly one of person or organization.");
    }
}

public sealed class ManufacturersInputValidator : AbstractValidator<ManufacturersInput>
{
    public ManufacturersInputValidator()
    {
        RuleFor(x => x.Manufacturers).NotNull();
        RuleForEach(x => x.Manufacturers).ChildRules(m =>
        {
            m.RuleFor(x => x.Organization).NotEmpty();
            m.RuleFor(x => x.From!).SetValidator(new FuzzyDateValidator()).When(x => x.From is not null);
            m.RuleFor(x => x.To!).SetValidator(new FuzzyDateValidator()).When(x => x.To is not null);
        });
    }
}

public sealed class RelationsInputValidator : AbstractValidator<RelationsInput>
{
    public RelationsInputValidator()
    {
        RuleFor(x => x.Relations).NotNull()
            .Must(r => r.Select(x => (x.Type, x.Firearm)).Distinct().Count() == r.Count)
            .WithMessage("Each relation (type and firearm) may appear once.");
        RuleForEach(x => x.Relations).ChildRules(r =>
        {
            r.RuleFor(x => x.Firearm).NotEmpty();
            r.RuleFor(x => x.Note).MaximumLength(1000);
        });
    }
}

public sealed class AdoptionsInputValidator : AbstractValidator<AdoptionsInput>
{
    public AdoptionsInputValidator()
    {
        RuleFor(x => x.Adoptions).NotNull();
        RuleForEach(x => x.Adoptions).ChildRules(a =>
        {
            a.RuleFor(x => x.Country).NotEmpty();
            a.RuleFor(x => x.From!).SetValidator(new FuzzyDateValidator()).When(x => x.From is not null);
            a.RuleFor(x => x.To!).SetValidator(new FuzzyDateValidator()).When(x => x.To is not null);
            a.RuleFor(x => x.Note).MaximumLength(1000);
        });
    }
}

public sealed class ChamberingsInputValidator : AbstractValidator<ChamberingsInput>
{
    public ChamberingsInputValidator() =>
        RuleFor(x => x.Chamberings).NotNull()
            .Must(c => c.Count(x => x.IsPrimary) <= 1).WithMessage("At most one chambering is primary.")
            .Must(c => c.Select(x => x.Cartridge).Distinct().Count() == c.Count).WithMessage("Each cartridge may appear once.");
}
