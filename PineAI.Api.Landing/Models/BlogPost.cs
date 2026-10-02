using System.ComponentModel.DataAnnotations;

namespace PineAI.Api.Landing.Models;

public class BlogPost
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [StringLength(100)]
    public string CategorySlug { get; set; } = string.Empty;

    [StringLength(500)]
    public string? TagsCsv { get; set; }

    [StringLength(1000)]
    public string? CoverImageUrl { get; set; }

    [StringLength(300)]
    public string? CoverImageAlt { get; set; }

    [StringLength(200)]
    public string AuthorName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? AuthorRole { get; set; }

    [StringLength(1000)]
    public string? AuthorAvatarUrl { get; set; }

    [StringLength(1000)]
    public string? AuthorBio { get; set; }

    // Stored and returned exactly as submitted — no parsing/sanitization
    // happens in this project.
    [Required]
    public string ContentHtml { get; set; } = string.Empty;

    public string? KeyTakeawaysJson { get; set; }
    public string? FaqsJson { get; set; }

    public int? ReadMinutes { get; set; }

    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
