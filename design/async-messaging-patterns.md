# Async Messaging Patterns and Instrumentation

## Overview

This document outlines the asynchronous messaging patterns, instrumentation, and resilience strategies for the agentic-demo IT support and purchasing system. These patterns ensure reliable, scalable, and observable agent-to-agent (A2A) communication, just as we would implement in a robust microservices architecture.

## Table of Contents

- [Core Messaging Patterns](#core-messaging-patterns)
- [Dead Letter Queues (DLQ)](#dead-letter-queues-dlq)
- [Short-Circuiting Patterns](#short-circuiting-patterns)
- [Queue Overload Management](#queue-overload-management)
- [Event Swarm Management](#event-swarm-management)
- [Instrumentation and Observability](#instrumentation-and-observability)
- [Implementation Guidelines](#implementation-guidelines)
- [Platform-Specific Considerations](#platform-specific-considerations)

---

## Core Messaging Patterns

### 1. Publish/Subscribe (Pub/Sub)

**Use Case**: Broadcasting ticket events to multiple interested agents.

**Example**: When a new ticket is created, multiple agents may need to be notified:
- Ticketing Triage Agent (to categorize)
- Analytics Agent (to record metrics)
- Notification Agent (to alert relevant parties)

**Implementation**:
```
Topic: ticket.created
Subscribers: [TriageAgent, AnalyticsAgent, NotificationAgent]

Message Structure:
{
  "eventType": "ticket.created",
  "eventId": "uuid",
  "timestamp": "2026-01-28T22:00:00Z",
  "payload": {
    "ticketId": "TKT-12345",
    "type": "support",
    "creator": "employee@company.com",
    "priority": "medium"
  }
}
```

**Benefits**:
- Loose coupling between publishers and subscribers
- Easy to add new subscribers without modifying publishers
- Supports parallel processing of events

**Considerations**:
- Each subscriber gets its own copy of the message
- Order of processing across subscribers is not guaranteed
- Subscribers must be idempotent (handle duplicate messages)

### 2. Request/Reply Pattern

**Use Case**: Synchronous-style communication where a response is expected.

**Example**: Purchasing Agent requests policy evaluation from Policy Engine.

**Implementation**:
```
Request Queue: policy.evaluation.requests
Reply Queue: policy.evaluation.replies.{correlationId}

Request Message:
{
  "correlationId": "uuid",
  "replyTo": "policy.evaluation.replies.{correlationId}",
  "timestamp": "2026-01-28T22:00:00Z",
  "payload": {
    "ticketId": "TKT-12345",
    "requestAmount": 5000,
    "vendor": "Dell",
    "department": "Engineering"
  }
}

Reply Message:
{
  "correlationId": "uuid",
  "timestamp": "2026-01-28T22:00:15Z",
  "payload": {
    "approved": false,
    "reason": "Exceeds department threshold",
    "requiresApproval": true,
    "approverLevel": "manager"
  }
}
```

**Benefits**:
- Clear request/response semantics
- Enables timeout handling
- Maintains conversation context via correlationId

**Considerations**:
- Implement timeout mechanisms (typically 30-60 seconds)
- Handle missing or delayed responses gracefully
- Use temporary reply queues to avoid queue proliferation

### 3. Competing Consumers Pattern

**Use Case**: Load balancing work across multiple instances of the same agent.

**Example**: Multiple Triage Agent instances processing incoming tickets.

**Implementation**:
```
Queue: tickets.triage.queue
Consumers: [TriageAgent-Instance-1, TriageAgent-Instance-2, TriageAgent-Instance-3]

Message Distribution: Round-robin or priority-based
Message Locking: Pessimistic lock with visibility timeout
```

**Benefits**:
- Horizontal scalability
- Improved throughput
- Built-in redundancy

**Considerations**:
- Messages must be processed independently (no ordering guarantees)
- Implement proper message locking/visibility timeouts
- Handle poison messages appropriately

### 4. Saga Pattern

**Use Case**: Coordinating multi-step workflows with compensation logic.

**Example**: Purchase fulfillment workflow.

**Implementation**:
```
Workflow Steps:
1. Reserve budget → Compensation: Release budget
2. Create purchase order → Compensation: Cancel PO
3. Submit to vendor → Compensation: Request cancellation
4. Track shipment → Compensation: N/A (informational)
5. Create delivery ticket → Compensation: Cancel delivery

Orchestration:
- Each step publishes success/failure events
- Orchestrator maintains workflow state
- On failure, triggers compensation steps in reverse order
```

**Benefits**:
- Maintains consistency across distributed operations
- Provides clear rollback mechanisms
- Enables complex multi-agent workflows

**Considerations**:
- Compensation logic must be carefully designed
- Some steps may not be fully reversible
- Maintain detailed audit logs for debugging

---

## Dead Letter Queues (DLQ)

Dead Letter Queues capture messages that cannot be processed successfully after multiple retry attempts, preventing message loss and enabling manual investigation.

### DLQ Strategy

**Configuration**:
```
Primary Queue: tickets.triage.queue
Dead Letter Queue: tickets.triage.dlq
Max Delivery Attempts: 5
DLQ Trigger: After 5 failed deliveries OR poison message detected
```

### Message Flow to DLQ

```
1. Message arrives in primary queue
2. Agent attempts to process → FAILURE
3. Message returns to queue with retry count++
4. Retry count < max? → Return to step 2
5. Retry count >= max? → Move to DLQ
```

### DLQ Message Properties

Messages in DLQ should include:
- Original message content
- Error details (exception message, stack trace)
- Retry history (timestamps, error messages for each attempt)
- Original queue name
- DeadLetterReason (MaxDeliveryCountExceeded, Expired, Rejected)
- EnqueuedTimestamp (when first received)
- DeadLetterTimestamp (when moved to DLQ)

**Example DLQ Message**:
```json
{
  "originalMessage": {
    "ticketId": "TKT-12345",
    "content": "..."
  },
  "dlqMetadata": {
    "deadLetterReason": "MaxDeliveryCountExceeded",
    "originalQueue": "tickets.triage.queue",
    "retryCount": 5,
    "firstAttempt": "2026-01-28T22:00:00Z",
    "lastAttempt": "2026-01-28T22:05:00Z",
    "errors": [
      {
        "attempt": 1,
        "timestamp": "2026-01-28T22:00:00Z",
        "error": "ValidationException: Missing required field 'category'"
      },
      // ... subsequent attempts
    ]
  }
}
```

### DLQ Monitoring and Recovery

**Monitoring**:
- Alert when DLQ depth exceeds threshold (e.g., 10 messages)
- Dashboard showing DLQ message count by queue
- Analyze DLQ messages for patterns (common error types)

**Recovery Workflow**:
1. **Investigation**: Review DLQ message and error details
2. **Root Cause Analysis**: Identify why processing failed
3. **Fix**: Deploy code fix or data correction
4. **Reprocessing**:
   - Manual: Admin tools to replay specific messages
   - Automated: Bulk replay after confirming fix
5. **Verification**: Confirm successful processing
6. **Clean-up**: Remove successfully processed messages from DLQ

**Reprocessing Strategies**:
- **Selective Replay**: Move specific messages back to primary queue
- **Batch Replay**: Bulk transfer after validation
- **Transform and Replay**: Fix message format before reprocessing
- **Archive**: Move permanently failed messages to cold storage

### DLQ Best Practices

1. **Separate DLQs per Queue**: Each primary queue should have its own DLQ for isolation
2. **Alert Immediately**: Set up alerts for any DLQ activity
3. **Regular Review**: Establish a process to review DLQ messages daily
4. **Poison Message Detection**: Quickly identify and handle malformed messages
5. **Retention Policy**: Define DLQ message retention (e.g., 30 days)
6. **Automated Analysis**: Use tools to analyze DLQ patterns and suggest fixes

---

## Short-Circuiting Patterns

Short-circuiting prevents cascading failures and resource exhaustion when downstream services or agents are unhealthy.

### Circuit Breaker Pattern

The circuit breaker monitors failures and temporarily blocks requests to failing services.

**States**:
1. **CLOSED**: Normal operation, requests flow through
2. **OPEN**: Service is failing, requests are immediately rejected
3. **HALF-OPEN**: Testing if service has recovered

**State Transitions**:
```
CLOSED → OPEN: 
  Trigger: Failure threshold reached (e.g., 5 failures in 10 seconds)
  Action: Stop sending requests, return cached data or error

OPEN → HALF-OPEN:
  Trigger: Timeout period elapsed (e.g., 60 seconds)
  Action: Allow limited test requests

HALF-OPEN → CLOSED:
  Trigger: Success threshold reached (e.g., 3 consecutive successes)
  Action: Resume normal operation

HALF-OPEN → OPEN:
  Trigger: Test request fails
  Action: Return to open state, restart timeout
```

**Implementation Example**:

```csharp
// Purchasing Agent calling External Vendor API
// NOTE: This is a simplified example for illustration purposes.
// Production implementations should use thread-safe operations and established libraries like Polly.
public class VendorApiCircuitBreaker
{
    private readonly object _lock = new object();
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private int _successCount = 0;
    private DateTime _lastFailureTime;
    private const int FailureThreshold = 5;
    private const int SuccessThreshold = 3;
    private const int TimeoutSeconds = 60;
    
    public async Task<VendorResponse> CallVendorApi(VendorRequest request)
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                if (DateTime.UtcNow - _lastFailureTime > TimeSpan.FromSeconds(TimeoutSeconds))
                {
                    _state = CircuitBreakerState.HalfOpen;
                    _successCount = 0;
                    _failureCount = 0;
                }
                else
                {
                    throw new CircuitBreakerOpenException("Vendor API circuit breaker is open");
                }
            }
        }
        
        try
        {
            var response = await _vendorApi.SubmitOrderAsync(request);
            
            lock (_lock)
            {
                if (_state == CircuitBreakerState.HalfOpen)
                {
                    _successCount++;
                    if (_successCount >= SuccessThreshold)
                    {
                        _state = CircuitBreakerState.Closed;
                        _successCount = 0;
                        _failureCount = 0;
                    }
                }
            }
            
            return response;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastFailureTime = DateTime.UtcNow;
                _failureCount++;
                
                if (_state == CircuitBreakerState.HalfOpen || 
                    _failureCount >= FailureThreshold)
                {
                    _state = CircuitBreakerState.Open;
                }
            }
            
            throw;
        }
    }
}
```

### Fallback Strategies

When circuit breaker opens, implement graceful degradation:

**1. Cached Response**:
```csharp
if (circuitBreakerOpen)
{
    return _cache.GetLastKnownGoodResponse(request);
}
```

**2. Default Response**:
```csharp
if (circuitBreakerOpen)
{
    return new VendorResponse 
    { 
        Status = "Unavailable",
        EstimatedDelivery = DateTime.UtcNow.AddDays(14) // Default lead time
    };
}
```

**3. Alternative Service**:
```csharp
if (primaryVendorCircuitOpen)
{
    return await _secondaryVendorApi.SubmitOrderAsync(request);
}
```

**4. Queue for Later**:
```csharp
if (circuitBreakerOpen)
{
    await _retryQueue.EnqueueAsync(request);
    return new VendorResponse 
    { 
        Status = "Queued",
        Message = "Order queued for processing when service recovers"
    };
}
```

### Bulkhead Pattern

Isolate resources to prevent failure in one area from affecting others.

**Example**: Separate thread pools for different agent operations:

```
Thread Pool Allocation:
- Triage Operations: 10 threads
- Purchasing Operations: 10 threads
- Fulfillment Operations: 10 threads
- External API Calls: 5 threads (isolated)

Benefit: If external API calls block, only 5 threads are affected,
         other operations continue normally.
```

**Message Queue Bulkheads**:
```
Separate queues by priority and criticality:
- tickets.critical.queue (high priority, more consumers)
- tickets.normal.queue (normal priority)
- tickets.low.queue (low priority, fewer consumers)

Benefit: Low priority message floods don't starve critical operations.
```

---

## Queue Overload Management

### Metrics and Thresholds

**Key Metrics to Monitor**:
- Queue depth (number of messages waiting)
- Message age (time in queue)
- Processing rate (messages/second)
- Consumer count (active processing agents)
- Error rate (percentage of failed messages)

**Example Thresholds**:
```yaml
tickets.triage.queue:
  normal: depth < 100
  warning: depth >= 100 && depth < 500
  critical: depth >= 500
  
  age:
    normal: < 30 seconds
    warning: >= 30 seconds && < 5 minutes
    critical: >= 5 minutes
```

### Alert Configuration

**Alert Levels**:

**1. INFO** - Queue approaching capacity:
```
Condition: Queue depth > 100
Action: Log event, update dashboard
Notification: None
```

**2. WARNING** - Queue backlog building:
```
Condition: Queue depth > 500 OR message age > 5 minutes
Action: Log event, update dashboard, prepare for scaling
Notification: Team Slack channel
```

**3. CRITICAL** - Queue overload:
```
Condition: Queue depth > 1000 OR message age > 15 minutes
Action: Log event, update dashboard, auto-scale if possible
Notification: PagerDuty alert, Team Slack channel
```

**Alert Example Message**:
```
🚨 CRITICAL: Queue Overload Detected

Queue: tickets.triage.queue
Current Depth: 1,247 messages
Oldest Message: 18 minutes
Processing Rate: 15 msg/sec
Consumers Active: 3
Expected Recovery: 83 minutes

Actions Taken:
- Scaling consumers from 3 to 6 (in progress)
- Low priority queue processing paused
- Administrators notified

Dashboard: https://monitoring.company.com/queues/tickets.triage
```

### Auto-Scaling Strategies

**Horizontal Scaling - Add More Consumers**:
```yaml
scaling_policy:
  metric: queue_depth
  scale_up:
    threshold: depth > 200
    action: add 2 consumers
    max_consumers: 10
    cooldown: 2 minutes
  
  scale_down:
    threshold: depth < 50 AND all consumers idle > 5 minutes
    action: remove 1 consumer
    min_consumers: 2
    cooldown: 10 minutes
```

**Vertical Scaling - Increase Processing Capacity**:
```yaml
instance_scaling:
  metric: cpu_utilization
  scale_up:
    threshold: cpu > 80%
    action: increase instance size
    max_instance_size: large
```

**Example Auto-Scaling Implementation**:
```csharp
public class QueueAutoScaler
{
    public async Task MonitorAndScaleAsync(string queueName)
    {
        var metrics = await _queueClient.GetMetricsAsync(queueName);
        
        if (metrics.Depth > 200 && _currentConsumerCount < _maxConsumers)
        {
            await ScaleUpAsync(queueName, consumerCountToAdd: 2);
            _logger.LogInformation(
                "Scaled up {QueueName}: added 2 consumers. New count: {Count}",
                queueName, _currentConsumerCount);
        }
        else if (metrics.Depth < 50 && 
                 _currentConsumerCount > _minConsumers &&
                 metrics.AllConsumersIdleFor > TimeSpan.FromMinutes(5))
        {
            await ScaleDownAsync(queueName, consumerCountToRemove: 1);
            _logger.LogInformation(
                "Scaled down {QueueName}: removed 1 consumer. New count: {Count}",
                queueName, _currentConsumerCount);
        }
    }
}
```

### Load Shedding

When the system is overwhelmed, selectively drop or defer low-priority work.

**Priority-Based Load Shedding**:
```csharp
public async Task<bool> EnqueueMessageAsync(Message message)
{
    var queueDepth = await _queueClient.GetDepthAsync();
    
    // Critical messages always enqueued
    if (message.Priority == Priority.Critical)
    {
        await _queueClient.SendAsync(message);
        return true;
    }
    
    // Drop low priority messages if queue overloaded
    if (queueDepth > 1000 && message.Priority == Priority.Low)
    {
        _logger.LogWarning(
            "Dropping low priority message due to queue overload. MessageId: {MessageId}",
            message.Id);
        
        // Optionally notify sender
        await NotifyMessageDroppedAsync(message);
        return false;
    }
    
    // Defer medium priority messages if severely overloaded
    if (queueDepth > 2000 && message.Priority == Priority.Medium)
    {
        await _deferredQueue.SendAsync(message);
        return true;
    }
    
    await _queueClient.SendAsync(message);
    return true;
}
```

### Backpressure Mechanisms

Prevent upstream systems from overwhelming the queue.

**Token Bucket Algorithm**:
```csharp
// NOTE: This is a simplified example. Production implementations should use thread-safe operations.
public class TokenBucket : IDisposable
{
    private readonly object _lock = new object();
    private readonly int _capacity = 100;
    private readonly int _refillRate = 10; // tokens per second
    private int _tokens = 100;
    private DateTime _lastRefill = DateTime.UtcNow;
    
    public Task<bool> TryConsumeAsync()
    {
        lock (_lock)
        {
            RefillTokens();
            
            if (_tokens > 0)
            {
                _tokens--;
                return Task.FromResult(true);
            }
            
            // Backpressure signal: no tokens available
            return Task.FromResult(false);
        }
    }
    
    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        var tokensToAdd = (int)(elapsedSeconds * _refillRate);
        
        _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
        _lastRefill = now;
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}
```

**HTTP 429 Response**:
```csharp
// REST API implementing backpressure
[HttpPost("tickets")]
public async Task<IActionResult> CreateTicket([FromBody] TicketRequest request)
{
    if (!await _rateLimiter.TryConsumeAsync())
    {
        return StatusCode(429, new 
        { 
            error = "Too many requests",
            retryAfter = 10 // seconds
        });
    }
    
    // Process ticket creation
}
```

---

## Event Swarm Management

Event swarms occur when a single action triggers a cascade of events, potentially overwhelming the system.

### Common Swarm Scenarios

**1. System Recovery After Downtime**:
```
Scenario: System comes back online after 1-hour outage
Result: 1000+ queued messages all processed simultaneously
Impact: Consumer overload, downstream service saturation
```

**2. Bulk Import Operations**:
```
Scenario: Importing 500 historical tickets
Result: 500 × ticket.created events
Impact: All agents receive 500 events simultaneously
```

**3. Cascading Updates**:
```
Scenario: Update to ticket affects linked tickets
Result: Each update triggers events on related tickets
Impact: Exponential event multiplication
```

### Swarm Prevention Strategies

**1. Rate Limiting Event Publication**:
```csharp
public class RateLimitedEventPublisher : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrent = 10;
    
    public RateLimitedEventPublisher()
    {
        _semaphore = new SemaphoreSlim(_maxConcurrent);
    }
    
    public async Task PublishBulkEventsAsync(IEnumerable<Event> events)
    {
        foreach (var eventBatch in events.Chunk(10))
        {
            await _semaphore.WaitAsync();
            
            try
            {
                await Task.WhenAll(eventBatch.Select(e => PublishEventAsync(e)));
            }
            finally
            {
                _semaphore.Release();
            }
            
            // Rate limit: 10 events per second
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
    
    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}
```

**2. Event Batching**:
```csharp
// Instead of publishing individual events
foreach (var ticket in tickets)
{
    await PublishEventAsync(new TicketCreatedEvent(ticket)); // DON'T DO THIS
}

// Publish a single batch event
await PublishEventAsync(new TicketsBatchCreatedEvent
{
    Tickets = tickets.Select(t => t.Id).ToList(),
    Count = tickets.Count
});
```

**3. Event Deduplication**:
```csharp
public class EventDeduplicator
{
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _deduplicationWindow = TimeSpan.FromMinutes(5);
    
    public async Task<bool> IsEventDuplicateAsync(Event evt)
    {
        var key = $"event:{evt.EventType}:{evt.AggregateId}:{evt.SequenceNumber}";
        var exists = await _cache.GetAsync(key);
        
        if (exists != null)
        {
            _logger.LogWarning(
                "Duplicate event detected: {EventType} for {AggregateId}",
                evt.EventType, evt.AggregateId);
            return true;
        }
        
        await _cache.SetAsync(
            key, 
            Encoding.UTF8.GetBytes("processed"),
            new DistributedCacheEntryOptions 
            { 
                AbsoluteExpirationRelativeToNow = _deduplicationWindow 
            });
        
        return false;
    }
}
```

**4. Throttled Processing with Windowing**:
```csharp
public class ThrottledEventProcessor
{
    private readonly int _windowSize = 100; // Process 100 events per window
    private readonly TimeSpan _windowDuration = TimeSpan.FromSeconds(10);
    
    public async Task ProcessEventSwarmAsync(List<Event> events)
    {
        _logger.LogInformation(
            "Processing event swarm with {Count} events", events.Count);
        
        for (int i = 0; i < events.Count; i += _windowSize)
        {
            var window = events.Skip(i).Take(_windowSize);
            var startTime = DateTime.UtcNow;
            
            await Task.WhenAll(window.Select(e => ProcessEventAsync(e)));
            
            var elapsed = DateTime.UtcNow - startTime;
            var delay = _windowDuration - elapsed;
            
            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug(
                    "Throttling: waiting {DelayMs}ms before next window",
                    delay.TotalMilliseconds);
                await Task.Delay(delay);
            }
        }
    }
}
```

### Swarm Detection

**Monitoring Anomalies**:
```csharp
public class EventSwarmDetector
{
    private readonly int _swarmThreshold = 100; // events in 10 seconds
    private readonly TimeSpan _detectionWindow = TimeSpan.FromSeconds(10);
    
    public async Task<bool> DetectSwarmAsync(string eventType)
    {
        var recentEvents = await _metricsStore.GetEventCountAsync(
            eventType, 
            DateTime.UtcNow - _detectionWindow);
        
        if (recentEvents > _swarmThreshold)
        {
            await AlertSwarmDetectedAsync(eventType, recentEvents);
            return true;
        }
        
        return false;
    }
    
    private async Task AlertSwarmDetectedAsync(string eventType, int count)
    {
        _logger.LogWarning(
            "Event swarm detected: {EventType}, Count: {Count} in {Seconds}s",
            eventType, count, _detectionWindow.TotalSeconds);
        
        // Trigger alerts, enable throttling, etc.
        await _alertingService.SendAlertAsync(new Alert
        {
            Severity = AlertSeverity.Warning,
            Title = $"Event Swarm Detected: {eventType}",
            Description = $"{count} events in {_detectionWindow.TotalSeconds} seconds",
            Timestamp = DateTime.UtcNow
        });
    }
}
```

### Swarm Recovery

**1. Temporary Event Buffering**:
```csharp
// Buffer events when swarm detected
if (await _swarmDetector.DetectSwarmAsync(eventType))
{
    await _bufferQueue.EnqueueAsync(evt);
    return;
}

// Process buffered events at controlled rate
public async Task DrainBufferAsync()
{
    while (await _bufferQueue.TryDequeueAsync(out var evt))
    {
        await ProcessEventAsync(evt);
        await Task.Delay(TimeSpan.FromMilliseconds(100)); // 10 events/sec
    }
}
```

**2. Circuit Breaker Activation**:
```csharp
// Temporarily stop accepting new events
if (await _swarmDetector.DetectSwarmAsync(eventType))
{
    _circuitBreaker.Open();
    
    // Return to senders to retry later
    return new EventPublishResult 
    { 
        Success = false,
        Reason = "Event swarm detected, system in protection mode",
        RetryAfter = TimeSpan.FromMinutes(5)
    };
}
```

---

## Instrumentation and Observability

### Logging Standards

**Structured Logging Format**:
```json
{
  "timestamp": "2026-01-28T22:00:00.000Z",
  "level": "INFO",
  "logger": "PurchasingAgent",
  "message": "Processing purchase request",
  "context": {
    "ticketId": "TKT-12345",
    "correlationId": "uuid-1234",
    "agentId": "purchasing-agent-1",
    "messageId": "msg-5678"
  },
  "metrics": {
    "processingTimeMs": 150,
    "queueDepth": 45
  }
}
```

**Log Levels and Usage**:

```
ERROR: System failures, exceptions, data loss
  Example: "Failed to process message after 5 retries"

WARN: Recoverable issues, degraded performance, approaching limits
  Example: "Queue depth exceeding 500 messages"

INFO: Significant business events, state transitions
  Example: "Purchase request approved and forwarded to fulfillment"

DEBUG: Detailed diagnostic information
  Example: "Policy evaluation result: approved by rule R-123"

TRACE: Very detailed information, typically disabled in production
  Example: "Message deserialized: {content}"
```

### Distributed Tracing

**Correlation IDs**:
```csharp
public class MessageHandler
{
    public async Task HandleMessageAsync(Message message)
    {
        // Extract or generate correlation ID
        var correlationId = message.Headers.GetValueOrDefault("CorrelationId") 
            ?? Guid.NewGuid().ToString();
        
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["MessageId"] = message.Id,
            ["MessageType"] = message.Type
        });
        
        _logger.LogInformation("Processing message");
        
        // Propagate correlation ID to downstream calls
        await _downstreamService.ProcessAsync(new Request
        {
            CorrelationId = correlationId,
            // ... other properties
        });
    }
}
```

**Trace Spans**:
```
Trace: Purchase Request Flow (CorrelationId: abc-123)
├─ Span: Receive Message [Duration: 2ms]
│  ├─ Span: Deserialize [Duration: 1ms]
│  └─ Span: Validate Schema [Duration: 1ms]
├─ Span: Process Purchase Request [Duration: 150ms]
│  ├─ Span: Evaluate Policy [Duration: 50ms]
│  │  ├─ Span: Check Budget [Duration: 10ms]
│  │  ├─ Span: Check Vendor [Duration: 15ms]
│  │  └─ Span: Check Department Limit [Duration: 25ms]
│  ├─ Span: Create Purchase Order [Duration: 30ms]
│  └─ Span: Publish Approved Event [Duration: 20ms]
└─ Span: Acknowledge Message [Duration: 3ms]

Total Duration: 155ms
```

**Trace Context Propagation**:
```csharp
// Publishing event with trace context
public async Task PublishEventAsync(Event evt, string correlationId, string traceId)
{
    var message = new Message
    {
        Body = JsonSerializer.Serialize(evt),
        Headers = new Dictionary<string, string>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = traceId,
            ["ParentSpanId"] = Activity.Current?.SpanId.ToString(),
            ["TraceState"] = Activity.Current?.TraceStateString
        }
    };
    
    await _messagingClient.SendAsync(message);
}
```

### Metrics and KPIs

**Message Processing Metrics**:
```csharp
public class MessageProcessingMetrics
{
    // Counters
    public Counter MessagesReceived { get; set; }
    public Counter MessagesProcessedSuccessfully { get; set; }
    public Counter MessagesFailed { get; set; }
    public Counter MessagesRetriedCount { get; set; }
    public Counter MessagesSentToDLQ { get; set; }
    
    // Histograms
    public Histogram ProcessingDuration { get; set; }
    public Histogram MessageSize { get; set; }
    public Histogram MessageAge { get; set; }
    
    // Gauges
    public Gauge ActiveConsumerCount { get; set; }
    public Gauge QueueDepth { get; set; }
    public Gauge ProcessingRate { get; set; }
}
```

**Business Metrics**:
```csharp
public class BusinessMetrics
{
    // Ticket lifecycle metrics
    public Counter TicketsCreated { get; set; }
    public Counter TicketsResolved { get; set; }
    public Histogram TicketResolutionTime { get; set; }
    
    // Purchase workflow metrics
    public Counter PurchaseRequestsSubmitted { get; set; }
    public Counter PurchaseRequestsAutoApproved { get; set; }
    public Counter PurchaseRequestsEscalated { get; set; }
    public Counter PurchaseRequestsRejected { get; set; }
    public Histogram PurchaseApprovalTime { get; set; }
    
    // Agent performance metrics
    public Histogram TriageAgentProcessingTime { get; set; }
    public Histogram PurchasingAgentProcessingTime { get; set; }
    public Counter TriageAgentErrors { get; set; }
}
```

### Dashboard Requirements

**Operational Dashboard**:
```
Real-time Monitoring:
┌─────────────────────────────────────────────────────────────┐
│ Queue Health Overview                                       │
├─────────────────────────────────────────────────────────────┤
│ tickets.triage.queue        [=====>    ] 247 msgs (Normal) │
│ tickets.purchasing.queue    [==>        ]  84 msgs (Normal) │
│ tickets.fulfillment.queue   [=          ]  12 msgs (Normal) │
│ tickets.triage.dlq          [>          ]   3 msgs (⚠ Warn) │
├─────────────────────────────────────────────────────────────┤
│ Processing Rates (msg/sec)                                  │
│ Triage: 45 ↑  Purchasing: 12 ↑  Fulfillment: 8 ↑          │
├─────────────────────────────────────────────────────────────┤
│ Active Consumers                                            │
│ Triage: 5  Purchasing: 3  Fulfillment: 2                   │
├─────────────────────────────────────────────────────────────┤
│ Circuit Breakers                                            │
│ Vendor API: CLOSED ✓  Policy Engine: CLOSED ✓              │
└─────────────────────────────────────────────────────────────┘
```

**Business Dashboard**:
```
Ticket Lifecycle Metrics (Last 24h):
┌─────────────────────────────────────────────────────────────┐
│ Tickets Created: 1,234    Resolved: 1,189    Backlog: 45   │
│ Avg Resolution Time: 4.2 hours                             │
│                                                             │
│ Purchase Requests:                                          │
│ ├─ Total: 234                                              │
│ ├─ Auto-Approved: 198 (85%)                                │
│ ├─ Escalated: 24 (10%)                                     │
│ └─ Rejected: 12 (5%)                                       │
│                                                             │
│ Avg Purchase Approval Time: 12 minutes                     │
└─────────────────────────────────────────────────────────────┘
```

### Alerting Rules

**Critical Alerts** (PagerDuty):
```yaml
- name: DLQ_Messages_Critical
  condition: dlq_depth > 50
  duration: 5 minutes
  severity: critical
  notification: pagerduty
  
- name: Queue_Depth_Critical
  condition: queue_depth > 1000
  duration: 10 minutes
  severity: critical
  notification: pagerduty

- name: Processing_Rate_Dropped
  condition: processing_rate < 10% of baseline
  duration: 5 minutes
  severity: critical
  notification: pagerduty
```

**Warning Alerts** (Slack):
```yaml
- name: Queue_Depth_High
  condition: queue_depth > 500
  duration: 5 minutes
  severity: warning
  notification: slack
  
- name: Message_Age_High
  condition: oldest_message_age > 5 minutes
  duration: 2 minutes
  severity: warning
  notification: slack

- name: Error_Rate_Elevated
  condition: error_rate > 5%
  duration: 10 minutes
  severity: warning
  notification: slack
```

---

## Implementation Guidelines

### Message Schema Design

**Schema Versioning**:
```json
{
  "schemaVersion": "1.0",
  "eventType": "ticket.created",
  "eventId": "uuid",
  "timestamp": "2026-01-28T22:00:00Z",
  "payload": {
    "ticketId": "TKT-12345",
    "type": "support",
    "priority": "medium"
  }
}
```

**Schema Evolution Rules**:
1. **Backward Compatible Changes** (safe):
   - Adding optional fields
   - Adding new event types
   - Deprecating fields (keep for compatibility)

2. **Breaking Changes** (requires new version):
   - Removing fields
   - Changing field types
   - Making optional fields required
   - Renaming fields

**Schema Registry**:
```
Maintain central schema registry:
- Store all message schemas
- Version control schemas
- Validate messages against schemas
- Provide schema documentation
```

### Error Handling Best Practices

**Error Categories**:

1. **Transient Errors** (retry):
   - Network timeouts
   - Database connection failures
   - Temporary service unavailability
   - Rate limit errors

2. **Permanent Errors** (don't retry):
   - Invalid message format
   - Schema validation failures
   - Business rule violations
   - Authentication/authorization failures

**Retry Strategy**:
```csharp
public async Task<bool> ProcessWithRetryAsync(Message message)
{
    var retryPolicy = Policy
        .Handle<TransientException>()
        .WaitAndRetryAsync(
            retryCount: 5,
            sleepDurationProvider: attempt => 
                TimeSpan.FromSeconds(Math.Pow(2, attempt)), // Exponential backoff
            onRetry: (exception, timeSpan, attempt, context) =>
            {
                _logger.LogWarning(
                    "Retry {Attempt}: {Exception} - waiting {Delay}s",
                    attempt, exception.Message, timeSpan.TotalSeconds);
            });
    
    try
    {
        await retryPolicy.ExecuteAsync(async () =>
        {
            await ProcessMessageAsync(message);
        });
        
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process message after retries");
        return false;
    }
}
```

### Idempotency

**Ensure messages can be processed multiple times safely**:

```csharp
public class IdempotentMessageHandler
{
    private readonly IDistributedCache _processedMessages;
    
    public async Task<bool> ProcessMessageIdempotentlyAsync(Message message)
    {
        var idempotencyKey = $"processed:{message.Id}";
        
        // Check if already processed
        var existing = await _processedMessages.GetAsync(idempotencyKey);
        if (existing != null)
        {
            _logger.LogInformation(
                "Message {MessageId} already processed, skipping",
                message.Id);
            return true;
        }
        
        // Process message
        var result = await ProcessMessageAsync(message);
        
        if (result.Success)
        {
            // Mark as processed
            await _processedMessages.SetAsync(
                idempotencyKey,
                Encoding.UTF8.GetBytes(result.ResultId),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });
        }
        
        return result.Success;
    }
}
```

**Database-Level Idempotency**:
```sql
-- Use unique constraints to prevent duplicate operations
CREATE TABLE ProcessedMessages (
    MessageId VARCHAR(100) PRIMARY KEY,
    ProcessedAt DATETIME NOT NULL,
    ResultId VARCHAR(100)
);

-- Insert with conflict handling (PostgreSQL syntax)
INSERT INTO ProcessedMessages (MessageId, ProcessedAt, ResultId)
VALUES (@MessageId, @ProcessedAt, @ResultId)
ON CONFLICT (MessageId) DO NOTHING;

-- For SQL Server, use:
-- IF NOT EXISTS (SELECT 1 FROM ProcessedMessages WHERE MessageId = @MessageId)
-- BEGIN
--     INSERT INTO ProcessedMessages (MessageId, ProcessedAt, ResultId)
--     VALUES (@MessageId, @ProcessedAt, @ResultId)
-- END
```

### Message Ordering

**Ordering Guarantees**:

1. **No Ordering** (most common):
   - Messages processed independently
   - Highest throughput
   - Use when order doesn't matter

2. **Partition/Session Ordering**:
   - Messages with same partition key processed in order
   - Example: All events for ticket TKT-12345 processed sequentially
   - Moderate throughput

3. **Global Ordering**:
   - All messages processed in strict order
   - Single consumer
   - Lowest throughput
   - Avoid if possible

**Implementation**:
```csharp
// Partition-based ordering
public async Task PublishEventAsync(Event evt)
{
    var message = new Message
    {
        Body = JsonSerializer.Serialize(evt),
        PartitionKey = evt.TicketId, // All events for same ticket go to same partition
        SessionId = evt.TicketId // For session-based ordering
    };
    
    await _messagingClient.SendAsync(message);
}
```

---

## Platform-Specific Considerations

### RabbitMQ

**DLQ Configuration**:
```csharp
var queueArgs = new Dictionary<string, object>
{
    ["x-dead-letter-exchange"] = "dlx",
    ["x-dead-letter-routing-key"] = "tickets.triage.dlq",
    ["x-message-ttl"] = 300000, // 5 minutes
    ["x-max-length"] = 10000
};

channel.QueueDeclare(
    queue: "tickets.triage.queue",
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: queueArgs);
```

**Circuit Breaker with RabbitMQ**:
```csharp
// Use channel prefetch for backpressure
channel.BasicQos(
    prefetchSize: 0,
    prefetchCount: 10, // Limit to 10 unacknowledged messages
    global: false);
```

**RabbitMQ Clustering**:
```
Configure for high availability:
- Quorum queues for durability
- Federated exchanges for geographic distribution
- Load balancer for connection distribution
```

### Azure Service Bus

**DLQ Configuration**:
```csharp
var queueOptions = new CreateQueueOptions("tickets.triage.queue")
{
    MaxDeliveryCount = 5,
    DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
    EnableDeadLetteringOnMessageExpiration = true,
    MaxSizeInMegabytes = 1024
};

await adminClient.CreateQueueAsync(queueOptions);
```

**Session-Based Ordering**:
```csharp
// Enable sessions for ordering guarantees
var queueOptions = new CreateQueueOptions("tickets.triage.queue")
{
    RequiresSession = true
};

// Send with session ID
var message = new ServiceBusMessage(body)
{
    SessionId = ticketId // All messages for ticket processed in order
};
```

**Auto-Scaling with Azure**:
```yaml
# Azure Service Bus Premium tier supports auto-scaling
scaling:
  min_messaging_units: 1
  max_messaging_units: 4
  scale_metric: active_messages
  scale_threshold: 1000 messages
```

### Message Size Limits

**Best Practices**:
```csharp
// Keep messages small (< 256 KB)
public class PurchaseRequestEvent
{
    public string TicketId { get; set; }
    public string Status { get; set; }
    
    // DON'T include large data
    // public byte[] ItemImage { get; set; }
    
    // DO use references
    public string ItemImageUrl { get; set; }
    public string DocumentStorageId { get; set; }
}

// For large payloads, use claim check pattern
public async Task PublishLargeMessageAsync(LargeMessage message)
{
    // Store large payload in blob storage
    var blobId = await _blobStorage.UploadAsync(message.LargePayload);
    
    // Publish small message with reference
    await _messagingClient.SendAsync(new Message
    {
        TicketId = message.TicketId,
        BlobId = blobId // Reference to large data
    });
}
```

---

## Testing Strategies

### Unit Testing Message Handlers

```csharp
[Test]
public async Task ProcessMessage_ValidMessage_Success()
{
    // Arrange
    var mockQueue = new Mock<IMessageQueue>();
    var handler = new TriageAgentHandler(mockQueue.Object);
    var message = new Message
    {
        TicketId = "TKT-123",
        Type = "support"
    };
    
    // Act
    var result = await handler.ProcessMessageAsync(message);
    
    // Assert
    Assert.IsTrue(result.Success);
    mockQueue.Verify(q => q.SendAsync(It.IsAny<Message>()), Times.Once);
}

[Test]
public async Task ProcessMessage_TransientError_Retries()
{
    // Arrange
    var mockQueue = new Mock<IMessageQueue>();
    mockQueue
        .SetupSequence(q => q.SendAsync(It.IsAny<Message>()))
        .ThrowsAsync(new TransientException())
        .ThrowsAsync(new TransientException())
        .ReturnsAsync(new SendResult { Success = true });
    
    var handler = new TriageAgentHandler(mockQueue.Object);
    var message = new Message { TicketId = "TKT-123" };
    
    // Act
    var result = await handler.ProcessMessageAsync(message);
    
    // Assert
    Assert.IsTrue(result.Success);
    mockQueue.Verify(q => q.SendAsync(It.IsAny<Message>()), Times.Exactly(3));
}
```

### Integration Testing

```csharp
[Test]
public async Task EndToEndWorkflow_PurchaseRequest_Success()
{
    // Arrange
    var testQueue = new TestMessageQueue();
    var triageAgent = new TriageAgent(testQueue);
    var purchaseAgent = new PurchaseAgent(testQueue);
    var fulfillmentAgent = new FulfillmentAgent(testQueue);
    
    // Act
    var ticketId = await CreatePurchaseRequestAsync();
    await testQueue.ProcessAllMessagesAsync();
    
    // Assert
    var ticket = await GetTicketAsync(ticketId);
    Assert.AreEqual("fulfilled", ticket.Status);
    
    // Verify message flow
    var messages = testQueue.GetProcessedMessages();
    Assert.Contains(messages, m => m.Type == "ticket.created");
    Assert.Contains(messages, m => m.Type == "purchase.approved");
    Assert.Contains(messages, m => m.Type == "order.fulfilled");
}
```

### Chaos Testing

```csharp
[Test]
public async Task ChaosTest_RandomFailures_SystemRecovers()
{
    // Inject random failures
    var chaosQueue = new ChaosMessageQueue(
        failureRate: 0.2, // 20% of messages fail
        transientErrorRate: 0.8 // 80% of failures are transient
    );
    
    var handler = new TriageAgentHandler(chaosQueue);
    
    // Process 100 messages
    var results = new List<ProcessResult>();
    for (int i = 0; i < 100; i++)
    {
        var result = await handler.ProcessMessageAsync(
            new Message { TicketId = $"TKT-{i}" });
        results.Add(result);
    }
    
    // Assert most messages eventually succeed
    var successRate = results.Count(r => r.Success) / 100.0;
    Assert.Greater(successRate, 0.95); // 95% success after retries
}
```

---

## Conclusion

This document provides comprehensive guidance for implementing robust asynchronous messaging patterns in the agentic-demo system. By following these patterns and best practices, the system will achieve:

- **Reliability**: Through DLQs, retries, and error handling
- **Resilience**: Via circuit breakers, bulkheads, and load shedding
- **Scalability**: Using auto-scaling and load balancing
- **Observability**: With comprehensive logging, metrics, and tracing
- **Maintainability**: Through clear patterns and instrumentation

Regular review and refinement of these patterns ensures the system remains robust as requirements evolve and scale increases.

---

## References and Additional Resources

- [Enterprise Integration Patterns](https://www.enterpriseintegrationpatterns.com/)
- [Azure Service Bus Documentation](https://learn.microsoft.com/azure/service-bus-messaging/)
- [RabbitMQ Best Practices](https://www.rabbitmq.com/best-practices.html)
- [Microsoft Agent Framework](https://learn.microsoft.com/azure/ai-services/agents/)
- [Distributed Tracing with OpenTelemetry](https://opentelemetry.io/)
- [Circuit Breaker Pattern](https://learn.microsoft.com/azure/architecture/patterns/circuit-breaker)
- [Retry Pattern](https://learn.microsoft.com/azure/architecture/patterns/retry)

---

*Document Version: 1.0*  
*Last Updated: 2026-01-28*  
*Owner: Architecture Team*
