using Ticketing.DataAccess.Entities;

namespace Ticketing.DataAccess.Seeding;

/// <summary>
/// Seeds demo data for the ticketing system.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(TicketingDbContext context)
    {
        // Only seed if no tickets exist
        if (context.Tickets.Any())
        {
            return;
        }

        var tickets = CreateDemoTickets();
        var history = CreateDemoHistory(tickets);

        await context.Tickets.AddRangeAsync(tickets);
        await context.TicketHistory.AddRangeAsync(history);
        await context.SaveChangesAsync();
    }

    private static List<TicketEntity> CreateDemoTickets()
    {
        var now = DateTime.UtcNow;

        return
        [
            // Help Desk - Open Tickets
            new TicketEntity
            {
                TicketId = "TKT-00001",
                Title = "Cannot access email on mobile device",
                Description = "Getting authentication error when trying to set up company email on my iPhone. Error message says 'Invalid credentials' but I know my password is correct.",
                TicketType = "support",
                Category = "software",
                Priority = "high",
                Status = "in-progress",
                AssignedQueue = "helpdesk",
                CreatedBy = "john.employee@company.com",
                CreatedByName = "John Employee",
                CreatedAt = now.AddHours(-4),
                UpdatedAt = now.AddHours(-2),
                TriageDecision = "helpdesk",
                TriageNotes = "Email configuration issue - routing to help desk for user assistance"
            },
            new TicketEntity
            {
                TicketId = "TKT-00002",
                Title = "Network printer not working",
                Description = "The 3rd floor printer (HP-3F-001) is showing offline. Multiple users have reported the issue.",
                TicketType = "support",
                Category = "hardware",
                Priority = "medium",
                Status = "triaged",
                AssignedQueue = "helpdesk",
                CreatedBy = "sarah.manager@company.com",
                CreatedByName = "Sarah Manager",
                CreatedAt = now.AddHours(-6),
                UpdatedAt = now.AddHours(-5),
                TriageDecision = "helpdesk",
                TriageNotes = "Hardware issue - needs on-site support"
            },
            new TicketEntity
            {
                TicketId = "TKT-00003",
                Title = "VPN connection drops frequently",
                Description = "When working from home, my VPN connection drops every 30-45 minutes. I have to reconnect manually each time.",
                TicketType = "support",
                Category = "network",
                Priority = "medium",
                Status = "in-progress",
                AssignedQueue = "helpdesk",
                AssignedTo = "sarah.helpdesk@company.com",
                CreatedBy = "bob.remote@company.com",
                CreatedByName = "Bob Remote Worker",
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddHours(-8),
                TriageDecision = "helpdesk",
                TriageNotes = "VPN connectivity issue - may need network team escalation"
            },

            // Purchasing - Pending Approval
            new TicketEntity
            {
                TicketId = "TKT-00004",
                Title = "Request new MacBook Pro 16\"",
                Description = "My current laptop is 5 years old and struggling with the development workload. Requesting MacBook Pro 16\" with M3 Max chip, 32GB RAM.",
                TicketType = "purchase",
                Category = "hardware",
                Priority = "medium",
                Status = "pending-approval",
                AssignedQueue = "purchasing",
                CreatedBy = "developer.alice@company.com",
                CreatedByName = "Alice Developer",
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now.AddDays(-1),
                TriageDecision = "purchasing",
                TriageNotes = "Hardware purchase request - requires manager approval"
            },
            new TicketEntity
            {
                TicketId = "TKT-00005",
                Title = "JetBrains All Products Pack License",
                Description = "Requesting annual license for JetBrains All Products Pack for development team. This includes IntelliJ IDEA, Rider, and other tools we need.",
                TicketType = "purchase",
                Category = "software",
                Priority = "low",
                Status = "pending-approval",
                AssignedQueue = "purchasing",
                CreatedBy = "developer.charlie@company.com",
                CreatedByName = "Charlie Lead Dev",
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now.AddDays(-2),
                TriageDecision = "purchasing",
                TriageNotes = "Software license request - pending approval"
            },

            // Approved and in fulfillment
            new TicketEntity
            {
                TicketId = "TKT-00006",
                Title = "Dell Monitor 27\" 4K - Approved",
                Description = "Need additional monitor for dual-screen setup. Dell UltraSharp 27\" 4K USB-C Hub Monitor.",
                TicketType = "purchase",
                Category = "hardware",
                Priority = "low",
                Status = "pending-fulfillment",
                AssignedQueue = "purchasing",
                CreatedBy = "analyst.diana@company.com",
                CreatedByName = "Diana Analyst",
                CreatedAt = now.AddDays(-5),
                UpdatedAt = now.AddDays(-3),
                TriageDecision = "purchasing",
                TriageNotes = "Hardware purchase - small item, auto-approved"
            },

            // Delivery ticket (child of purchase)
            new TicketEntity
            {
                TicketId = "TKT-00007",
                Title = "Deliver new keyboard to user",
                Description = "Logitech MX Keys has arrived. Please deliver to Diana Analyst at desk 3B-205.",
                TicketType = "delivery",
                Category = "hardware",
                Priority = "low",
                Status = "in-progress",
                AssignedQueue = "helpdesk",
                ParentTicketId = "TKT-00006",
                CreatedBy = "fulfillment@company.com",
                CreatedByName = "Fulfillment Team",
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddHours(-12),
                TriageDecision = "helpdesk",
                TriageNotes = "Delivery task for fulfilled purchase order"
            },

            // Closed tickets for history
            new TicketEntity
            {
                TicketId = "TKT-00008",
                Title = "Password reset request",
                Description = "Forgot my password and locked out of account.",
                TicketType = "support",
                Category = "access",
                Priority = "high",
                Status = "closed",
                AssignedQueue = "helpdesk",
                CreatedBy = "new.employee@company.com",
                CreatedByName = "New Employee",
                CreatedAt = now.AddDays(-7),
                UpdatedAt = now.AddDays(-7).AddHours(1),
                ClosedAt = now.AddDays(-7).AddHours(1),
                TriageDecision = "helpdesk",
                TriageNotes = "Standard password reset",
                ResolutionNotes = "Password reset via self-service portal. Verified user identity and sent reset link."
            },
            new TicketEntity
            {
                TicketId = "TKT-00009",
                Title = "Software installation - Visual Studio",
                Description = "Need Visual Studio 2022 Enterprise installed on my workstation.",
                TicketType = "support",
                Category = "software",
                Priority = "medium",
                Status = "closed",
                AssignedQueue = "helpdesk",
                CreatedBy = "developer.frank@company.com",
                CreatedByName = "Frank Developer",
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-9),
                ClosedAt = now.AddDays(-9),
                TriageDecision = "helpdesk",
                TriageNotes = "Standard software install request",
                ResolutionNotes = "Visual Studio 2022 Enterprise installed and activated. User confirmed working."
            },

            // A new ticket awaiting triage
            new TicketEntity
            {
                TicketId = "TKT-00010",
                Title = "Request for standing desk",
                Description = "Would like to request a standing desk converter for ergonomic reasons. Doctor has recommended it.",
                TicketType = "purchase",
                Category = "hardware",
                Priority = "medium",
                Status = "new",
                CreatedBy = "health.conscious@company.com",
                CreatedByName = "Health Conscious Employee",
                CreatedAt = now.AddMinutes(-30),
                UpdatedAt = now.AddMinutes(-30)
            }
        ];
    }

    private static List<TicketHistoryEntity> CreateDemoHistory(List<TicketEntity> tickets)
    {
        var now = DateTime.UtcNow;
        var history = new List<TicketHistoryEntity>();

        foreach (var ticket in tickets)
        {
            // Add creation entry
            history.Add(new TicketHistoryEntity
            {
                TicketId = ticket.TicketId,
                FieldName = "Created",
                NewValue = $"Ticket created: {ticket.Title}",
                ChangedBy = ticket.CreatedBy,
                ChangedAt = ticket.CreatedAt,
                ChangeReason = "New ticket created"
            });

            // Add status change if not new
            if (ticket.Status != "new")
            {
                history.Add(new TicketHistoryEntity
                {
                    TicketId = ticket.TicketId,
                    FieldName = "Status",
                    OldValue = "new",
                    NewValue = ticket.Status == "triaged" ? "triaged" : "triaged",
                    ChangedBy = "triage-agent@system.com",
                    ChangedAt = ticket.CreatedAt.AddMinutes(15),
                    ChangeReason = "Automated triage"
                });

                // Add queue assignment
                if (ticket.AssignedQueue != null)
                {
                    history.Add(new TicketHistoryEntity
                    {
                        TicketId = ticket.TicketId,
                        FieldName = "AssignedQueue",
                        OldValue = null,
                        NewValue = ticket.AssignedQueue,
                        ChangedBy = "triage-agent@system.com",
                        ChangedAt = ticket.CreatedAt.AddMinutes(15),
                        ChangeReason = "Automated routing"
                    });
                }
            }

            // Add closed entry if closed
            if (ticket.Status == "closed" && ticket.ClosedAt.HasValue)
            {
                history.Add(new TicketHistoryEntity
                {
                    TicketId = ticket.TicketId,
                    FieldName = "Status",
                    OldValue = "in-progress",
                    NewValue = "closed",
                    ChangedBy = "sarah.helpdesk@company.com",
                    ChangedAt = ticket.ClosedAt.Value,
                    ChangeReason = "Ticket resolved"
                });
            }
        }

        return history;
    }
}
