using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PineAI.Api.Landing.Auth;
using PineAI.Api.Landing.Data;
using PineAI.Api.Landing.Models;

namespace PineAI.Api.Landing.Controllers;

public class BlogPostUpsertRequest
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string CategorySlug { get; set; } = string.Empty;
    public string? TagsCsv { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? CoverImageAlt { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorRole { get; set; }
    public string? AuthorAvatarUrl { get; set; }
    public string? AuthorBio { get; set; }
    public string ContentHtml { get; set; } = string.Empty;
    public string? KeyTakeawaysJson { get; set; }
    public string? FaqsJson { get; set; }
    public int? ReadMinutes { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class BlogController : ControllerBase
{
    private const int DefaultTake = 20;
    private const int MaxTake = 50;

    private readonly LandingBlogDbContext db;

    public BlogController(LandingBlogDbContext db)
    {
        this.db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BlogPost>>> GetList(
        [FromQuery] int take = DefaultTake,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        var effectiveTake = take > MaxTake ? MaxTake : (take < 0 ? DefaultTake : take);
        var effectiveSkip = skip < 0 ? 0 : skip;

        var posts = await db.BlogPosts
            .Where(p => p.IsPublished)
            .OrderByDescending(p => p.PublishedAtUtc)
            .Skip(effectiveSkip)
            .Take(effectiveTake)
            .ToListAsync(ct);

        return Ok(posts);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<BlogPost>> GetBySlug(string slug, CancellationToken ct)
    {
        var post = await db.BlogPosts
            .FirstOrDefaultAsync(p => p.IsPublished && p.Slug.ToLower() == slug.ToLower(), ct);

        if (post is null)
            return NotFound();

        return Ok(post);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    public async Task<ActionResult<BlogPost>> Create([FromBody] BlogPostUpsertRequest request, CancellationToken ct)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(validationError);

        if (await SlugExistsAsync(request.Slug, excludeId: null, ct))
            return Conflict($"A post with slug '{request.Slug}' already exists.");

        var post = new BlogPost
        {
            CreatedAtUtc = DateTime.UtcNow
        };
        Apply(request, post);

        db.BlogPosts.Add(post);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetBySlug), new { slug = post.Slug }, post);
    }

    [HttpPut("{id:int}")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    public async Task<ActionResult<BlogPost>> Update(int id, [FromBody] BlogPostUpsertRequest request, CancellationToken ct)
    {
        var post = await db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
            return NotFound();

        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(validationError);

        if (await SlugExistsAsync(request.Slug, excludeId: id, ct))
            return Conflict($"A post with slug '{request.Slug}' already exists.");

        Apply(request, post);
        post.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Ok(post);
    }

    [HttpDelete("{id:int}")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var post = await db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
            return NotFound();

        db.BlogPosts.Remove(post);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static string? Validate(BlogPostUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
            return "Slug is required.";
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Title is required.";
        return null;
    }

    private Task<bool> SlugExistsAsync(string slug, int? excludeId, CancellationToken ct)
    {
        var query = db.BlogPosts.Where(p => p.Slug.ToLower() == slug.ToLower());
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    private static void Apply(BlogPostUpsertRequest request, BlogPost post)
    {
        post.Slug = request.Slug;
        post.Title = request.Title;
        post.Description = request.Description;
        post.Category = request.Category;
        post.CategorySlug = request.CategorySlug;
        post.TagsCsv = request.TagsCsv;
        post.CoverImageUrl = request.CoverImageUrl;
        post.CoverImageAlt = request.CoverImageAlt;
        post.AuthorName = request.AuthorName;
        post.AuthorRole = request.AuthorRole;
        post.AuthorAvatarUrl = request.AuthorAvatarUrl;
        post.AuthorBio = request.AuthorBio;
        post.ContentHtml = request.ContentHtml;
        post.KeyTakeawaysJson = request.KeyTakeawaysJson;
        post.FaqsJson = request.FaqsJson;
        post.ReadMinutes = request.ReadMinutes;
        post.IsPublished = request.IsPublished;
        post.PublishedAtUtc = request.PublishedAtUtc;
    }
}
