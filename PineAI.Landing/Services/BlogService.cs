using System.Net;
using System.Net.Http.Json;

namespace PineAI.Landing.Services;

public class BlogPostDto
{
    public int Id { get; set; }
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

public class BlogService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<BlogService> logger;

    public BlogService(HttpClient httpClient, ILogger<BlogService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<List<BlogPostDto>> GetListAsync(int take = 20, int skip = 0, CancellationToken ct = default)
    {
        try
        {
            var posts = await httpClient.GetFromJsonAsync<List<BlogPostDto>>(
                $"api/blog?take={take}&skip={skip}", ct);
            return posts ?? new List<BlogPostDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load blog post list");
            return new List<BlogPostDto>();
        }
    }

    public async Task<BlogPostDto?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<BlogPostDto>($"api/blog/{slug}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load blog post {Slug}", slug);
            return null;
        }
    }
}
