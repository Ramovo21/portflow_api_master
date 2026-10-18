using PortFlow.Identity;
using PortFlow.Shared;
using PortFlow.Shared.Config;
using PortFlow.Workflow;

// Profile = môi trường ASP.NET: api (mặc định) | worker | dev | test (= SPRING_PROFILES_ACTIVE của bản Java).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "api",
});
builder.Configuration.AddLegacyEnvironmentVariables();

builder.AddPortFlowShared();
builder.AddPortFlowIdentity();
builder.AddPortFlowWorkflow();

var app = builder.Build();
app.UsePortFlowShared();
app.Run();

public partial class Program;
