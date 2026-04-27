var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");

var hirebot = postgres.AddDatabase("hirebot");

var apiService = builder.AddProject<Projects.HireBot_ApiService>("hirebot-api")
    .WithReference(hirebot);

var app = builder.Build();
    
await app.RunAsync();
