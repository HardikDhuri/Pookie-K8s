
using Microsoft.EntityFrameworkCore;

namespace PookieApi;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<Message> Messages { get; set; }
}
