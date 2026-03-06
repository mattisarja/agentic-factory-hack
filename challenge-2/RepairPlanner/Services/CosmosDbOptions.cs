namespace RepairPlanner.Services;

public sealed class CosmosDbOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    public string TechniciansContainerName { get; set; } = "Technicians";
    public string PartsInventoryContainerName { get; set; } = "PartsInventory";
    public string WorkOrdersContainerName { get; set; } = "WorkOrders";
}
