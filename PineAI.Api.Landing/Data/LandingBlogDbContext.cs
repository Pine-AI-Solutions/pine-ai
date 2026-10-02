using Microsoft.EntityFrameworkCore;
using PineAI.Api.Landing.Models;

namespace PineAI.Api.Landing.Data;

public class LandingBlogDbContext : DbContext
{
    public LandingBlogDbContext(DbContextOptions<LandingBlogDbContext> options) : base(options) { }

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlogPost>(entity =>
        {
            entity.HasIndex(p => p.Slug).IsUnique();
        });
    }
}
