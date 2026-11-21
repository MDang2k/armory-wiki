using System.Linq.Expressions;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Media;
using ArmoryWiki.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArmoryWiki.Api.Features.Admin.Media;

/// <summary>Multipart upload: the file plus its attribution. All but the caption are required.</summary>
public sealed class ImageUploadForm
{
    [FromForm(Name = "file")] public IFormFile? File { get; init; }
    [FromForm(Name = "caption")] public string? Caption { get; init; }
    [FromForm(Name = "author")] public string? Author { get; init; }
    [FromForm(Name = "license")] public string? License { get; init; }
    [FromForm(Name = "sourceUrl")] public string? SourceUrl { get; init; }
}

public sealed record ImageDetailsInput(string? Caption, string Author, string License, string SourceUrl);

public sealed record ImageOrderInput(IReadOnlyList<int> ImageIds);

public sealed record AdminImageDto(int Id, int SortOrder, string ContentHash, ImageDto Image);

public sealed record VideoInput(string Url, string Title, string? Channel, DateTimeOffset? PublishedAt);

public sealed record AdminVideoDto(int Id, VideoStatus Status, VideoDto Video);

/// <summary>
/// Images of firearms, people, organizations and cartridges, and videos of firearms. Like citations they are
/// plain add/remove entries that don't take the subject's version.
/// </summary>
public static class AdminMediaEndpoints
{
    private sealed record Owner(
        string Route,
        string Title,
        Func<ArmoryWikiDbContext, int, CancellationToken, Task<bool>> ExistsAsync,
        Func<int, Expression<Func<Image, bool>>> ImagesOf,
        Action<Image, int> Attach);

    private static readonly Owner[] Owners =
    [
        new("firearms", "Firearm", (db, id, ct) => db.Firearms.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => i => i.FirearmId == id, (i, id) => i.FirearmId = id),
        new("people", "Person", (db, id, ct) => db.People.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => i => i.PersonId == id, (i, id) => i.PersonId = id),
        new("organizations", "Organization", (db, id, ct) => db.Organizations.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => i => i.OrganizationId == id, (i, id) => i.OrganizationId = id),
        new("cartridges", "Cartridge", (db, id, ct) => db.Cartridges.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => i => i.CartridgeId == id, (i, id) => i.CartridgeId = id),
    ];

    public static void MapAdminMedia(this RouteGroupBuilder admin)
    {
        var images = admin.MapGroup("").WithTags("Admin: images");
        foreach (var owner in Owners)
        {
            images.MapGet($"/{owner.Route}/{{id:int}}/images",
                (int id, ArmoryWikiDbContext db, CancellationToken ct) => ListImagesAsync(owner, id, db, ct))
                .ProducesProblem(StatusCodes.Status404NotFound);
            images.MapPost($"/{owner.Route}/{{id:int}}/images",
                (int id, [FromForm] ImageUploadForm form, ArmoryWikiDbContext db, FileStore store, CancellationToken ct) =>
                    UploadAsync(owner, id, form, db, store, ct))
                .Validate<ImageUploadForm>()
                .DisableAntiforgery() // form binding would demand the form-token middleware; the admin group checks the header token
                .WithMetadata(new RequestSizeLimitAttribute(ImageProcessor.MaxBytes + (1024 * 1024)))
                .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
            images.MapPut($"/{owner.Route}/{{id:int}}/images/order",
                (int id, ImageOrderInput input, ArmoryWikiDbContext db, CancellationToken ct) => ReorderAsync(owner, id, input, db, ct))
                .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        }

        images.MapPut("/images/{imageId:int}", UpdateImage).Validate<ImageDetailsInput>().ProducesProblem(StatusCodes.Status404NotFound);
        images.MapDelete("/images/{imageId:int}", DeleteImage).ProducesProblem(StatusCodes.Status404NotFound);

        var videos = admin.MapGroup("").WithTags("Admin: videos");
        videos.MapGet("/firearms/{id:int}/videos", ListVideos).ProducesProblem(StatusCodes.Status404NotFound);
        videos.MapPost("/firearms/{id:int}/videos", AddVideo).Validate<VideoInput>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        videos.MapPost("/videos/{videoId:int}/approve", (int videoId, ArmoryWikiDbContext db, CancellationToken ct) => SetVideoStatusAsync(videoId, VideoStatus.Approved, db, ct))
            .ProducesProblem(StatusCodes.Status404NotFound);
        videos.MapPost("/videos/{videoId:int}/reject", (int videoId, ArmoryWikiDbContext db, CancellationToken ct) => SetVideoStatusAsync(videoId, VideoStatus.Rejected, db, ct))
            .ProducesProblem(StatusCodes.Status404NotFound);
        videos.MapDelete("/videos/{videoId:int}", DeleteVideo).ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>Removes the files of each hash no image row uses any more (images share files by content hash).</summary>
    public static async Task DeleteUnusedImageFilesAsync(this ArmoryWikiDbContext db, FileStore store, IEnumerable<string> hashes, CancellationToken ct)
    {
        foreach (var hash in hashes.Distinct())
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockHashAsync(db, hash, ct);
            if (!await db.Images.AnyAsync(i => i.ContentHash == hash, ct))
            {
                store.Delete(hash);
            }

            await tx.CommitAsync(ct);
        }
    }

    /// <summary>
    /// Serializes everything that touches one hash's files (upload, delete, cleanup) until the transaction ends,
    /// so a delete never removes files an upload of the same bytes is about to use.
    /// </summary>
    internal static Task LockHashAsync(ArmoryWikiDbContext db, string hash, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hash}, 0))", ct);

    // Images ---------------------------------------------------------------------------------------------------

    private static AdminImageDto ToAdminDto(Image i) => new(i.Id, i.SortOrder, i.ContentHash, ImageDto.From(i));

    private static async Task<Results<Ok<List<AdminImageDto>>, ProblemHttpResult>> ListImagesAsync(
        Owner owner, int id, ArmoryWikiDbContext db, CancellationToken ct)
    {
        if (!await owner.ExistsAsync(db, id, ct))
        {
            return AdminProblems.NotFound(owner.Title, id);
        }

        var images = await db.Images.Where(owner.ImagesOf(id)).OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync(ct);
        return TypedResults.Ok(images.Select(ToAdminDto).ToList());
    }

    private static async Task<Results<Created<AdminImageDto>, ProblemHttpResult>> UploadAsync(
        Owner owner, int id, ImageUploadForm form, ArmoryWikiDbContext db, FileStore store, CancellationToken ct)
    {
        if (!await owner.ExistsAsync(db, id, ct))
        {
            return AdminProblems.NotFound(owner.Title, id);
        }

        ProcessedImage processed;
        try
        {
            using var buffer = new MemoryStream();
            await form.File!.CopyToAsync(buffer, ct);
            processed = ImageProcessor.Process(buffer.ToArray());
        }
        catch (InvalidImageException e)
        {
            return AdminProblems.BadRequest("Unusable image", e.Message);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockHashAsync(db, processed.Hash, ct);
        if (!store.Has(processed.Hash))
        {
            await store.WriteAsync(processed.Hash, processed.Files, ct);
        }

        var next = await db.Images.Where(owner.ImagesOf(id)).Select(i => (int?)i.SortOrder).MaxAsync(ct) is { } max ? max + 1 : 0;
        var image = new Image
        {
            ContentHash = processed.Hash,
            OriginalExtension = processed.OriginalExtension,
            Width = processed.Width,
            Height = processed.Height,
            Caption = form.Caption,
            Author = form.Author!,
            License = form.License!,
            SourceUrl = form.SourceUrl!,
            SortOrder = next,
        };
        owner.Attach(image, id);
        db.Images.Add(image);
        ProblemHttpResult? conflict;
        try
        {
            conflict = await db.TrySaveAsync<Image>(null, ct);
        }
        catch
        {
            await AbandonAsync(db, tx, store, processed.Hash);
            throw;
        }

        if (conflict is not null)
        {
            await AbandonAsync(db, tx, store, processed.Hash);
            return conflict;
        }

        await tx.CommitAsync(ct);
        return TypedResults.Created($"/api/admin/{owner.Route}/{id}/images", ToAdminDto(image));
    }

    /// <summary>Takes the owner's image ids in their new order; the first becomes the thumbnail.</summary>
    private static async Task<Results<Ok<List<AdminImageDto>>, ProblemHttpResult>> ReorderAsync(
        Owner owner, int id, ImageOrderInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        if (!await owner.ExistsAsync(db, id, ct))
        {
            return AdminProblems.NotFound(owner.Title, id);
        }

        var images = await db.Images.AsTracking().Where(owner.ImagesOf(id)).ToListAsync(ct);
        if (input.ImageIds is null || input.ImageIds.Count != images.Count || !images.Select(i => i.Id).ToHashSet().SetEquals(input.ImageIds))
        {
            return AdminProblems.BadRequest("Wrong image list", "Send every image of this subject exactly once, in the new order.");
        }

        var position = input.ImageIds.Select((imageId, index) => (imageId, index)).ToDictionary(x => x.imageId, x => x.index);
        foreach (var image in images)
        {
            image.SortOrder = position[image.Id];
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(images.OrderBy(i => i.SortOrder).Select(ToAdminDto).ToList());
    }

    private static async Task<Results<Ok<AdminImageDto>, ProblemHttpResult>> UpdateImage(
        int imageId, ImageDetailsInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var image = await db.Images.AsTracking().FirstOrDefaultAsync(i => i.Id == imageId, ct);
        if (image is null)
        {
            return AdminProblems.NotFound("Image", imageId);
        }

        image.Caption = input.Caption;
        image.Author = input.Author;
        image.License = input.License;
        image.SourceUrl = input.SourceUrl;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToAdminDto(image));
    }

    /// <summary>The files go too, unless another image uses the same content.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteImage(
        int imageId, ArmoryWikiDbContext db, FileStore store, CancellationToken ct)
    {
        var hash = await db.Images.Where(i => i.Id == imageId).Select(i => i.ContentHash).FirstOrDefaultAsync(ct);
        if (hash is null)
        {
            return AdminProblems.NotFound("Image", imageId);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockHashAsync(db, hash, ct);
        if (await db.Images.Where(i => i.Id == imageId).ExecuteDeleteAsync(ct) == 0)
        {
            return AdminProblems.NotFound("Image", imageId);
        }

        if (!await db.Images.AnyAsync(i => i.ContentHash == hash, ct))
        {
            store.Delete(hash);
        }

        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// A failed save leaves the transaction aborted: roll it back, then drop the files unless a committed image
    /// uses them. A concurrent upload of the same bytes holds the hash lock until it commits, so it is never cut off.
    /// </summary>
    private static async Task AbandonAsync(ArmoryWikiDbContext db, IDbContextTransaction tx, FileStore store, string hash)
    {
        await tx.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        await db.DeleteUnusedImageFilesAsync(store, [hash], CancellationToken.None);
    }

    // Videos ---------------------------------------------------------------------------------------------------

    private static readonly Expression<Func<Video, AdminVideoDto>> ToAdminVideoDto =
        v => new AdminVideoDto(v.Id, v.Status, new VideoDto(v.YouTubeVideoId, v.Title, v.Channel, v.PublishedAt));

    private static async Task<Results<Ok<List<AdminVideoDto>>, ProblemHttpResult>> ListVideos(int id, ArmoryWikiDbContext db, CancellationToken ct)
    {
        if (!await db.Firearms.IgnoreQueryFilters().AnyAsync(f => f.Id == id, ct))
        {
            return AdminProblems.NotFound("Firearm", id);
        }

        return TypedResults.Ok(await db.Videos.IgnoreQueryFilters().Where(v => v.FirearmId == id)
            .OrderBy(v => v.Status).ThenByDescending(v => v.PublishedAt).ThenBy(v => v.Id)
            .Select(ToAdminVideoDto).ToListAsync(ct));
    }

    /// <summary>A video an admin adds by hand is approved straight away.</summary>
    private static async Task<Results<Created<AdminVideoDto>, ProblemHttpResult>> AddVideo(
        int id, VideoInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        if (!await db.Firearms.IgnoreQueryFilters().AnyAsync(f => f.Id == id, ct))
        {
            return AdminProblems.NotFound("Firearm", id);
        }

        YouTube.TryParseId(input.Url, out var youTubeId); // the validator already accepted the URL
        if (await db.Videos.IgnoreQueryFilters().AnyAsync(v => v.FirearmId == id && v.YouTubeVideoId == youTubeId, ct))
        {
            return AdminProblems.Conflict("Video already added", "This firearm already has that video.");
        }

        var video = new Video
        {
            FirearmId = id,
            YouTubeVideoId = youTubeId!,
            Title = input.Title,
            Channel = input.Channel,
            PublishedAt = input.PublishedAt?.UtcDateTime,
            Status = VideoStatus.Approved,
        };
        db.Videos.Add(video);
        if (await db.TrySaveAsync<Video>(null, ct) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Created($"/api/admin/firearms/{id}/videos",
            await db.Videos.IgnoreQueryFilters().Where(v => v.Id == video.Id).Select(ToAdminVideoDto).SingleAsync(ct));
    }

    private static async Task<Results<Ok<AdminVideoDto>, ProblemHttpResult>> SetVideoStatusAsync(
        int videoId, VideoStatus status, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var video = await db.Videos.IgnoreQueryFilters().AsTracking().FirstOrDefaultAsync(v => v.Id == videoId, ct);
        if (video is null)
        {
            return AdminProblems.NotFound("Video", videoId);
        }

        video.Status = status;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await db.Videos.IgnoreQueryFilters().Where(v => v.Id == videoId).Select(ToAdminVideoDto).SingleAsync(ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteVideo(int videoId, ArmoryWikiDbContext db, CancellationToken ct) =>
        await db.Videos.IgnoreQueryFilters().Where(v => v.Id == videoId).ExecuteDeleteAsync(ct) > 0
            ? TypedResults.NoContent()
            : AdminProblems.NotFound("Video", videoId);
}

// Validators ---------------------------------------------------------------------------------------------------

internal static class AttributionRules
{
    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public const string LicenseMessage = "License must be one of: CC0-1.0, PD, CC-BY-2.0/2.5/3.0/4.0, CC-BY-SA-2.0/2.5/3.0/4.0.";
}

public sealed class ImageUploadFormValidator : AbstractValidator<ImageUploadForm>
{
    public ImageUploadFormValidator()
    {
        RuleFor(x => x.File).NotNull()
            .Must(f => f!.Length is > 0 and <= ImageProcessor.MaxBytes).WithMessage("The file must be between 1 byte and 20 MB.");
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x.Author).NotEmpty().MaximumLength(300);
        RuleFor(x => x.License).NotEmpty().Must(l => ImageLicenses.Find(l) is not null).WithMessage(AttributionRules.LicenseMessage);
        RuleFor(x => x.SourceUrl).NotEmpty().MaximumLength(2000).Must(AttributionRules.IsHttpUrl).WithMessage("SourceUrl must be an absolute http(s) address.");
    }
}

public sealed class ImageDetailsInputValidator : AbstractValidator<ImageDetailsInput>
{
    public ImageDetailsInputValidator()
    {
        RuleFor(x => x.Caption).MaximumLength(500);
        RuleFor(x => x.Author).NotEmpty().MaximumLength(300);
        RuleFor(x => x.License).NotEmpty().Must(l => ImageLicenses.Find(l) is not null).WithMessage(AttributionRules.LicenseMessage);
        RuleFor(x => x.SourceUrl).NotEmpty().MaximumLength(2000).Must(AttributionRules.IsHttpUrl).WithMessage("SourceUrl must be an absolute http(s) address.");
    }
}

public sealed class VideoInputValidator : AbstractValidator<VideoInput>
{
    public VideoInputValidator()
    {
        RuleFor(x => x.Url).NotEmpty().Must(u => YouTube.TryParseId(u, out _)).WithMessage("Url must be a YouTube video link.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Channel).MaximumLength(200);
    }
}
