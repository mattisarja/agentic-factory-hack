using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class CosmosDbService : IDisposable
{
    private readonly CosmosClient _client;
    private readonly Container _techniciansContainer;
    private readonly Container _partsContainer;
    private readonly Container _workOrdersContainer;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new ArgumentException("Cosmos DB endpoint is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Key))
        {
            throw new ArgumentException("Cosmos DB key is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.DatabaseName))
        {
            throw new ArgumentException("Cosmos DB database name is required.", nameof(options));
        }

        _client = new CosmosClient(options.Endpoint, options.Key);
        var database = _client.GetDatabase(options.DatabaseName);
        _techniciansContainer = database.GetContainer(options.TechniciansContainerName);
        _partsContainer = database.GetContainer(options.PartsInventoryContainerName);
        _workOrdersContainer = database.GetContainer(options.WorkOrdersContainerName);
    }

    public async Task<IReadOnlyList<Technician>> GetAvailableTechniciansWithSkillsAsync(
        IReadOnlyList<string> requiredSkills,
        CancellationToken ct = default)
    {
        try
        {
            var normalizedSkills = requiredSkills
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .Select(static s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedSkills.Length == 0)
            {
                _logger.LogInformation("No required skills provided; returning available technicians only.");
            }

            var query = new QueryDefinition("SELECT * FROM c WHERE c.isAvailable = true");
            var iterator = _techniciansContainer.GetItemQueryIterator<Technician>(query);

            var candidates = new List<Technician>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                candidates.AddRange(response);
            }

            var matches = candidates
                .Where(t => normalizedSkills.Length == 0 || TechnicianHasAllSkills(t, normalizedSkills))
                .OrderByDescending(t => CountMatchingSkills(t, normalizedSkills))
                .ThenBy(t => t.Name)
                .ToList();

            _logger.LogInformation(
                "Found {MatchCount} available technicians for {SkillCount} required skills.",
                matches.Count,
                normalizedSkills.Length);

            return matches;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(
                ex,
                "Cosmos query failed while retrieving available technicians by skills.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while retrieving available technicians by skills.");
            throw;
        }
    }

    public async Task<IReadOnlyList<Part>> GetPartsByPartNumbersAsync(
        IReadOnlyList<string> partNumbers,
        CancellationToken ct = default)
    {
        try
        {
            var normalizedPartNumbers = partNumbers
                .Where(static p => !string.IsNullOrWhiteSpace(p))
                .Select(static p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedPartNumbers.Length == 0)
            {
                _logger.LogInformation("No part numbers provided; returning empty parts list.");
                return [];
            }

            var queryText = "SELECT * FROM c WHERE ARRAY_CONTAINS(@partNumbers, c.partNumber)";
            var query = new QueryDefinition(queryText).WithParameter("@partNumbers", normalizedPartNumbers);
            var iterator = _partsContainer.GetItemQueryIterator<Part>(query);

            var parts = new List<Part>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(ct);
                parts.AddRange(response);
            }

            _logger.LogInformation(
                "Fetched {PartCount} parts for {RequestedCount} requested part numbers.",
                parts.Count,
                normalizedPartNumbers.Length);

            return parts;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos query failed while fetching parts by part numbers.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching parts by part numbers.");
            throw;
        }
    }

    public async Task<WorkOrder> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workOrder);

        try
        {
            workOrder.Id = string.IsNullOrWhiteSpace(workOrder.Id) ? Guid.NewGuid().ToString("N") : workOrder.Id;
            workOrder.WorkOrderNumber = string.IsNullOrWhiteSpace(workOrder.WorkOrderNumber)
                ? $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}"
                : workOrder.WorkOrderNumber;

            // ??= means "assign if null" (like Python's: x = x or default_value)
            workOrder.Status ??= "open";
            workOrder.CreatedAt = workOrder.CreatedAt == default ? DateTimeOffset.UtcNow : workOrder.CreatedAt;
            workOrder.UpdatedAt = DateTimeOffset.UtcNow;

            var response = await _workOrdersContainer.CreateItemAsync(
                item: workOrder,
                partitionKey: new PartitionKey(workOrder.Status),
                cancellationToken: ct);

            _logger.LogInformation(
                "Created work order {WorkOrderNumber} (id: {WorkOrderId}) with status {Status}.",
                response.Resource.WorkOrderNumber,
                response.Resource.Id,
                response.Resource.Status);

            return response.Resource;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos write failed while creating work order {WorkOrderId}.", workOrder.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating work order {WorkOrderId}.", workOrder.Id);
            throw;
        }
    }

    // Matching on all required skills keeps technician assignment deterministic.
    private static bool TechnicianHasAllSkills(Technician technician, IReadOnlyList<string> requiredSkills)
    {
        var skills = technician.Skills ?? [];
        return requiredSkills.All(required => skills.Any(skill =>
            string.Equals(skill, required, StringComparison.OrdinalIgnoreCase)));
    }

    private static int CountMatchingSkills(Technician technician, IReadOnlyList<string> requiredSkills)
    {
        var skills = technician.Skills ?? [];
        return requiredSkills.Count(required => skills.Any(skill =>
            string.Equals(skill, required, StringComparison.OrdinalIgnoreCase)));
    }

    public void Dispose() => _client.Dispose();
}
