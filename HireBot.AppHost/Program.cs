var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("Postgres");

var db = postgres.AddDatabase("HireBot");

var apiService = builder.AddProject<Projects.HireBot_ApiService>("api")
    .WithReference(db);

var app = builder.Build();
    
await app.RunAsync();
