using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace CallLimiter
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            if (args != null && args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
            {
                var assembly = Assembly.GetEntryAssembly();
                if (assembly != null)
                {
					var version = assembly.GetName().Version;
					if (version != null)
                    {
                        Console.WriteLine($"CallLimiter {version.Major}.{version.Minor}.{version.Build}");
                    }
                    else
                    {
                        Console.WriteLine("CallLimiter AssemblyInformationalVersionAttribute not found");
                    }
                }
                else
                {
                    Console.WriteLine("CallLimiter Entry Assembly not found");
                }
                return;
            }


            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, config) =>
                {
					var processModule = Process.GetCurrentProcess().MainModule;
					if (processModule != null)
					{
						string? directoryName = Path.GetDirectoryName(processModule.FileName);
						if (directoryName != null)
						{
							config.SetBasePath(directoryName);
						}
					}

					config.AddJsonFile("nlog.json", optional: true);
                }).ConfigureLogging((hostCotext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddNLog(new NLogLoggingConfiguration(hostCotext.Configuration.GetSection("NLog")));
                })
                .UseSystemd()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}