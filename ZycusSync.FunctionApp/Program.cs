using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using ZycusSync.Application.Users;
using ZycusSync.Domain.Abstractions;
using ZycusSync.Infrastructure.Config;
using ZycusSync.Infrastructure.Graph;
using ZycusSync.Infrastructure.Sinks;
using ZycusSync.Infrastructure.Storage;
using ZycusSync.Infrastructure.State;
using ZycusSync.Infrastructure.Options;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true)
           .AddEnvironmentVariables()
           .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // Options
        services.Configure<GraphOptions>(config.GetSection("Graph"));
        services.Configure<DeltaStorageOptions>(config.GetSection("DeltaStorage"));

        var graph = config.GetSection("Graph").Get<GraphOptions>()
                   ?? throw new InvalidOperationException("Missing Graph section.");
        services.AddSingleton(graph);

        // Http + Graph client
        services.AddHttpClient<IGraphClient, GraphClient>();

        // Delta store: Blob if connection string is present, else file
        var deltaOpt = config.GetSection("DeltaStorage").Get<DeltaStorageOptions>() ?? new();
        if (!string.IsNullOrWhiteSpace(deltaOpt.ConnectionString))
        {
            services.AddSingleton<IDeltaStore>(_ =>
                new BlobDeltaStore(deltaOpt.ConnectionString!, deltaOpt.Container, deltaOpt.Prefix));
        }
        else
        {
            var stateRoot = Path.Combine(AppContext.BaseDirectory, ".state");
            Directory.CreateDirectory(stateRoot);
            services.AddSingleton<IDeltaStore>(_ => new FileDeltaStore(stateRoot));
        }

        // Sink (noop for now – produces CSV-shaped rows via ToDictionary)
        services.AddSingleton<IZycusSink, NoopZycusSink>();

        // MediatR – scan Application assembly
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ProcessUserDelta).Assembly));
    })
    .Build();

host.Run();
