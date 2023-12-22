using CallLimiterWeb.Areas.Identity;
using CallLimiterWeb.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using Radzen;
using StackExchange.Redis;
using System.Reflection;

namespace CallLimiterWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {

			if (args != null && args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
			{
				VersionService vs = new VersionService();
				string ver = vs.GetVersion();
					
				if (ver != null)
				{
					Console.WriteLine($"CallLimiterWeb {ver}");
				}
				else
				{
					Console.WriteLine("CallLimiterWeb AssemblyInformationalVersionAttribute not found");
				}
				return;
			}


			var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
			var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());


			builder.Services.AddSingleton(builder.Configuration);

            builder.Services.AddScoped<VersionService>();

            // NLog log to file only
            builder.Logging.ClearProviders();
			builder.Host.UseNLog();

			// mariaDB
			var serverVersion = new MySqlServerVersion(new Version(10, 6, 12));
            builder.Services.AddDbContextFactory<DataContext>(options =>
            {
                options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"), serverVersion);
            });

            // redis
			var redisConnectionString = builder.Configuration.GetSection("Redis:ConnectionStrings").Value;
			builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
				 ConnectionMultiplexer.Connect(new ConfigurationOptions
				 {
					 EndPoints = { redisConnectionString! },
					 SyncTimeout = 10000,  // 10 seconds    
					 ConnectTimeout = 10000,  // 10 seconds
					 AsyncTimeout = 10000,  // 10 seconds  
					 AbortOnConnectFail = false,
				 }));


			// Add Identity services  
			builder.Services.AddDefaultIdentity<IdentityUser>().AddEntityFrameworkStores<DataContext>();
            builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();
            // Enable Cookie Authorize
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();

            // DNIS Service
            builder.Services.AddScoped<IDNISService, DNISService>();

            // Blazor
			builder.Services.AddScoped<DialogService>();

            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();

            builder.Services.AddControllers();

            builder.Services.AddSession();


			builder.Services.AddHttpContextAccessor();

            builder.Services.AddScoped<HttpClient>(serviceProvider =>
            {
                var uriHelper = serviceProvider.GetRequiredService<NavigationManager>();

                return new HttpClient
                {
                    BaseAddress = new Uri(uriHelper.BaseUri)
                };
            });

            builder.Services.AddHttpClient();

			var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseSession();

			app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBlazorHub();
                endpoints.MapRazorPages();
                endpoints.MapFallbackToPage("/_Host");
            });

            app.Run();
        }

        public class VersionService
        {
            public string GetVersion()
            {
                var assembly = Assembly.GetEntryAssembly();
                if (assembly != null)
                {
					var version = assembly.GetName().Version;
					if (version != null)
                    {

                        string ver = version.Major.ToString()+"."+version.Minor.ToString()+"."+version.Build.ToString();
                        return ver;
					}
                    else
                    {
                        return "Unknown";
                    }
                }
                else
                {
                    return "Unknown";
                }
            }
        }

    }
}