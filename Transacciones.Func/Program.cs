using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .Build();

await host.RunAsync();

/// <summary>
/// Marcador para las pruebas de integracion del Function App.
/// </summary>
public partial class Program;