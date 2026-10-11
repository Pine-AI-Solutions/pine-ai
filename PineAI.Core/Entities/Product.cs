namespace PineAI.Core.Entities;

public class Product : IBaseEntity
{
    [Key]
    public int Id { get; set; }

    [StringLength(64)]
    public string? ProductCode { get; set; }

    [Required]
    [StringLength(256)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Category { get; set; } = string.Empty;

    [Required]
    public int AvailableCount { get; set; }

    [StringLength(32)]
    public string? Size { get; set; }

    [Required]
    public decimal Price { get; set; }

    [StringLength(128)]
    public string? FabricType { get; set; }

    [StringLength(64)]
    public string? Color { get; set; }

    [StringLength(128)]
    public string? Brand { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public DateTime UpdatedAt { get; set; }
}
