var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.WebApplication1>("webapp");

builder.Build().Run();
