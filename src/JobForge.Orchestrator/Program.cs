var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "jobforge-orchestrator");
app.Run();

public partial class Program;
