using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ticketing.DataAccess;

/// <summary>
/// Factory for creating TicketingDbContext at design time for EF Core migrations.
/// </summary>
public class TicketingDbContextFactory : IDesignTimeDbContextFactory<TicketingDbContext>
{
    public TicketingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TicketingDbContext>();
        
        // Use LocalDB for development/migrations
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=TicketingDb;Trusted_Connection=True;MultipleActiveResultSets=true");

        return new TicketingDbContext(optionsBuilder.Options);
    }
}
