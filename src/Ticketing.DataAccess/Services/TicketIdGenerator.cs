using Microsoft.EntityFrameworkCore;

namespace Ticketing.DataAccess.Services;

/// <summary>
/// Service for generating sequential ticket IDs.
/// </summary>
public interface ITicketIdGenerator
{
    Task<string> GenerateTicketIdAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Database-backed ticket ID generator.
/// </summary>
public class TicketIdGenerator : ITicketIdGenerator
{
    private readonly TicketingDbContext _context;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public TicketIdGenerator(TicketingDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateTicketIdAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Get the highest ticket number currently in use
            var lastTicket = await _context.Tickets
                .OrderByDescending(t => t.TicketId)
                .Select(t => t.TicketId)
                .FirstOrDefaultAsync(cancellationToken);

            int nextNumber = 1;
            if (lastTicket != null && lastTicket.StartsWith("TKT-"))
            {
                if (int.TryParse(lastTicket[4..], out var currentNumber))
                {
                    nextNumber = currentNumber + 1;
                }
            }

            return $"TKT-{nextNumber:D5}";
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
