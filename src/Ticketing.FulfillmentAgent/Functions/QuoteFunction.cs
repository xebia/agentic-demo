using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Ticketing.FulfillmentAgent.Models;
using Ticketing.FulfillmentAgent.Services;

namespace Ticketing.FulfillmentAgent.Functions;

public class QuoteFunction
{
    private readonly VendorApiClient _vendorClient;
    private readonly ILogger<QuoteFunction> _logger;

    public QuoteFunction(VendorApiClient vendorClient, ILogger<QuoteFunction> logger)
    {
        _vendorClient = vendorClient;
        _logger = logger;
    }

    [Function("GetQuote")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "fulfillment/quote")]
        HttpRequest req,
        CancellationToken cancellationToken)
    {
        QuoteRequest? quoteRequest;
        try
        {
            quoteRequest = await req.ReadFromJsonAsync<QuoteRequest>(cancellationToken);
        }
        catch
        {
            return new BadRequestObjectResult("Invalid request body");
        }

        if (quoteRequest?.Items == null || quoteRequest.Items.Count == 0)
        {
            return new BadRequestObjectResult("Items list is required");
        }

        _logger.LogInformation(
            "Processing quote request for ticket {TicketId} with {ItemCount} items",
            quoteRequest.TicketId, quoteRequest.Items.Count);

        var lineItems = new List<QuoteLineItem>();

        foreach (var itemDescription in quoteRequest.Items)
        {
            var products = await _vendorClient.SearchCatalogAsync(itemDescription, cancellationToken);

            if (products.Count > 0)
            {
                var bestMatch = products[0];
                lineItems.Add(new QuoteLineItem
                {
                    Sku = bestMatch.Sku,
                    Name = bestMatch.Name,
                    UnitPrice = bestMatch.Price,
                    Quantity = 1,
                    Available = true
                });
            }
            else
            {
                lineItems.Add(new QuoteLineItem
                {
                    Sku = "UNKNOWN",
                    Name = itemDescription,
                    UnitPrice = 0,
                    Quantity = 1,
                    Available = false
                });
            }
        }

        var response = new QuoteResponse
        {
            LineItems = lineItems,
            TotalEstimate = lineItems.Where(i => i.Available).Sum(i => i.UnitPrice * i.Quantity),
            Available = lineItems.Any(i => i.Available)
        };

        _logger.LogInformation(
            "Quote for ticket {TicketId}: {Total:C}, {Available}/{Total2} items available",
            quoteRequest.TicketId, response.TotalEstimate,
            lineItems.Count(i => i.Available), lineItems.Count);

        return new OkObjectResult(response);
    }
}
