using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

var loggerFactory = LoggerFactory.Create(builder =>
{
	builder
		.SetMinimumLevel(LogLevel.Information)
		.AddSimpleConsole(options =>
		{
			options.SingleLine = true;
			options.TimestampFormat = "HH:mm:ss ";
		});
});

var logger = loggerFactory.CreateLogger("RepairPlannerProgram");

var cosmosOptions = new CosmosDbOptions
{
	Endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? string.Empty,
	Key = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? string.Empty,
	DatabaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? string.Empty,
};

if (string.IsNullOrWhiteSpace(cosmosOptions.Endpoint)
	|| string.IsNullOrWhiteSpace(cosmosOptions.Key)
	|| string.IsNullOrWhiteSpace(cosmosOptions.DatabaseName))
{
	logger.LogWarning(
		"Missing one or more required Cosmos DB environment variables: COSMOS_ENDPOINT, COSMOS_KEY, COSMOS_DATABASE_NAME.");
	logger.LogInformation("Set environment variables and rerun to execute the full workflow.");
	return;
}

var faultMapping = new FaultMappingService();
using var cosmosDb = new CosmosDbService(cosmosOptions, loggerFactory.CreateLogger<CosmosDbService>());

var sampleFault = new DiagnosedFault
{
	Id = Guid.NewGuid().ToString("N"),
	FaultId = $"fault-{DateTime.UtcNow:yyyyMMddHHmmss}",
	MachineId = "machine-tire-press-01",
	FaultType = "curing_temperature_excessive",
	Severity = "high",
	Confidence = 0.93,
	Symptoms = ["curing_temperature above threshold", "temperature trend increasing"],
	RecommendedAction = "Inspect heating element and temperature sensor calibration.",
};

logger.LogInformation(
	"Starting repair planning workflow for fault {FaultType} on machine {MachineId}.",
	sampleFault.FaultType,
	sampleFault.MachineId);

var requiredSkills = faultMapping.GetRequiredSkills(sampleFault.FaultType);
var requiredPartNumbers = faultMapping.GetRequiredParts(sampleFault.FaultType);

logger.LogInformation("Required skills: {Skills}", string.Join(", ", requiredSkills));
logger.LogInformation("Required part numbers: {PartNumbers}", string.Join(", ", requiredPartNumbers));

var technicians = await cosmosDb.GetAvailableTechniciansWithSkillsAsync(requiredSkills);
var parts = await cosmosDb.GetPartsByPartNumbersAsync(requiredPartNumbers);

var selectedTechnician = technicians.FirstOrDefault();

var tasks = BuildTasks(requiredSkills);
var estimatedDuration = tasks.Sum(task => task.EstimatedDurationMinutes);

var workOrder = new WorkOrder
{
	MachineId = sampleFault.MachineId,
	Title = $"Repair plan for {sampleFault.FaultType}",
	Description = $"Generated from diagnosed fault {sampleFault.FaultId} with severity {sampleFault.Severity}.",
	Type = sampleFault.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "emergency" : "corrective",
	Priority = sampleFault.Severity,
	AssignedTo = selectedTechnician?.TechnicianId,
	Notes = sampleFault.RecommendedAction,
	EstimatedDuration = estimatedDuration,
	Tasks = tasks,
	PartsUsed = parts.Select(part => new WorkOrderPartUsage
	{
		PartId = part.Id,
		PartNumber = part.PartNumber,
		Quantity = 1,
	}).ToList(),
};

// ??= means "assign if null" (like Python's: x = x or default_value)
workOrder.Status ??= "open";

var savedWorkOrder = await cosmosDb.CreateWorkOrderAsync(workOrder);

logger.LogInformation("Repair planning workflow completed successfully.");
logger.LogInformation("Work order number: {WorkOrderNumber}", savedWorkOrder.WorkOrderNumber);
logger.LogInformation("Assigned technician: {AssignedTo}", savedWorkOrder.AssignedTo ?? "unassigned");
logger.LogInformation("Task count: {TaskCount}", savedWorkOrder.Tasks.Count);
logger.LogInformation("Parts count: {PartCount}", savedWorkOrder.PartsUsed.Count);

static List<RepairTask> BuildTasks(IReadOnlyList<string> requiredSkills)
{
	var tasks = new List<RepairTask>
	{
		new()
		{
			Sequence = 1,
			Title = "Safety lockout and inspection",
			Description = "Perform lockout/tagout and inspect the affected subsystem.",
			EstimatedDurationMinutes = 20,
			RequiredSkills = requiredSkills.Take(2).ToList(),
			SafetyNotes = "Follow lockout/tagout procedure before opening machine panels.",
		},
		new()
		{
			Sequence = 2,
			Title = "Replace or calibrate faulty components",
			Description = "Replace suspected parts and recalibrate sensors/controls.",
			EstimatedDurationMinutes = 45,
			RequiredSkills = requiredSkills.Take(4).ToList(),
			SafetyNotes = "Use insulated tools for any electrical diagnostics.",
		},
		new()
		{
			Sequence = 3,
			Title = "Validation run",
			Description = "Run validation cycle and verify metrics return to normal range.",
			EstimatedDurationMinutes = 30,
			RequiredSkills = requiredSkills.ToList(),
			SafetyNotes = "Keep clear of moving parts during validation cycle.",
		},
	};

	return tasks;
}
