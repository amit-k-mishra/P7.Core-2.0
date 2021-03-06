﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

using AspNetCoreRateLimit;
using GraphQL.Language.AST;
using IdentityServer4.Models;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using P7.AspNetCore.Identity.InMemory;
using P7.Core;
using P7.Core.FileProviders;
using P7.Core.Identity;
using P7.Core.IoC;
using P7.Core.Middleware;
using P7.Core.Scheduler.Scheduling;
using P7.Core.Startup;
using P7.Core.TagHelpers;
using P7.GraphQLCore.Stores;
using P7.IdentityServer4.Common;
using P7.IdentityServer4.Common.ExtensionGrantValidator;
using P7.IdentityServer4.Common.Middleware;
using P7.IdentityServer4.Common.Stores;
using P7.Razor.FileProvider;
using P7.RazorProvider.Store.Core;
using P7.RazorProvider.Store.Core.Interfaces;
using P7.TwitterAuthentication;
using ReferenceWebApp.Health;
using ReferenceWebApp.Models;
using ReferenceWebApp.Services;
 
using Serilog;
using ReferenceWebApp.InMemory;

namespace ReferenceWebApp
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        public Startup(IConfiguration configuration, IHostingEnvironment env)
        {
            _hostingEnvironment = env;
            P7.Core.Global.HostingEnvironment = _hostingEnvironment;
            Configuration = configuration;

            var appDataPath = Path.Combine(env.ContentRootPath, "App_Data");

            var RollingPath = Path.Combine(env.ContentRootPath, "logs/myapp-{Date}.txt");
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.RollingFile(RollingPath)
                .WriteTo.LiterateConsole()
                .CreateLogger();
            Log.Information("Ah, there you are!");

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings-ratelimiting.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings-healthcheck.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings-filters.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings-filters-graphql.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.IdentityServer4.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings-external-views.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            if (env.IsDevelopment())
            {
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                builder.AddUserSecrets<Startup>();
            }


            builder.AddEnvironmentVariables();
            Configuration = builder.Build();

            // Initialize the global configuration static
            GlobalConfigurationRoot.Configuration = Configuration;
        }

        public IConfiguration Configuration { get; }

        private void ConfigureRateLimitingServices(IServiceCollection services)
        {

            //load general configuration from appsettings.json
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));

            //load ip rules from appsettings.json
            services.Configure<IpRateLimitPolicies>(Configuration.GetSection("IpRateLimitPolicies"));

            // inject counter and rules stores
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();

        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddExtensionGrantValidator<PublicRefreshTokenExtensionGrantValidator>(); ;

            // needed to store rate limit counters and ip rules
            services.AddMemoryCache();

            ConfigureRateLimitingServices(services);

            services.TryAddSingleton(typeof(IStringLocalizerFactory), typeof(ResourceManagerStringLocalizerFactory));
            services.AddLocalization();

            var inMemoryStore = new InMemoryStore<ApplicationUser, ApplicationRole>();

            services.AddSingleton<IUserStore<ApplicationUser>>(provider =>
            {
                return inMemoryStore;
            });
            services.AddSingleton<IUserRoleStore<ApplicationUser>>(provider =>
            {
                return inMemoryStore;
            });
            services.AddSingleton<IRoleStore<ApplicationRole>>(provider =>
            {
                return inMemoryStore;
            });
            services.AddIdentity<ApplicationUser, ApplicationRole>()
                .AddDefaultTokenProviders()
                .AddIdentityServer();

            services.AddAuthentication<ApplicationUser>(Configuration);

            services
                .AddScoped
                <Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<ApplicationUser>,
                    AppClaimsPrincipalFactory<ApplicationUser,ApplicationRole>>();
                  
            // Add application services.
            services.AddTransient<IEmailSender, EmailSender>();

            services.AddAntiforgery(opts => opts.HeaderName = "X-XSRF-Token");
            services.AddMyHealthCheck(Configuration);
            services.AddMvc(opts =>
            {
                opts.Filters.AddService(typeof(AngularAntiforgeryCookieResultFilter));
            }).AddJsonOptions(options =>
            {
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            });
            services.AddTransient<AngularAntiforgeryCookieResultFilter>();

            var razorLocationStore = new RemoteRazorLocationStore();
            services.AddSingleton<IRemoteRazorLocationStore>(razorLocationStore);
            services.AddSingleton<IRazorLocationStore>(razorLocationStore);
            services.AddSingleton<RemoteRazorLocationStore>(razorLocationStore);
            services.Configure<RazorViewEngineOptions>(opts =>
                opts.FileProviders.Add(
                    new RazorFileProvider(razorLocationStore)
                )
            );

            services.AddAuthorization();

            services.AddLogging();
            services.AddWebEncoders();
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
            });

            services.AddDistributedMemoryCache();
            services.AddSession();

            services.AddTransient<ClaimsPrincipal>(
                s => s.GetService<IHttpContextAccessor>().HttpContext.User);
      
            services.AddAllConfigureServicesRegistrants(Configuration);
            services.AddDependenciesUsingAutofacModules();

            services.AddScheduler((sender, args) =>
            {
                Console.Write(args.Exception.Message);
                args.SetObserved();
            });

            var serviceProvider = services.BuildServiceProvider(Configuration);

            P7.Core.Global.ServiceProvider = serviceProvider;

            return serviceProvider;
        }
        private async Task LoadGraphQLAuthority()
        {
            var graphQLFieldAuthority = P7.Core.Global.ServiceProvider.GetServices<InMemoryGraphQLFieldAuthority>().FirstOrDefault();

            await graphQLFieldAuthority.AddClaimsAsync(OperationType.Mutation, "/blog", new List<Claim>()
            {
                new Claim(ClaimTypes.NameIdentifier,""),
                new Claim("client_id","resource-owner-client"),
            });
            await graphQLFieldAuthority.AddClaimsAsync(OperationType.Query, "/accessCode", new List<Claim>()
            {
                new Claim("x-graphql-auth","")
            });
        }
        private async Task LoadIdentityServer4Data()
        {
            var privateStore = P7.Core.Global.ServiceProvider.GetServices<InMemoryPrivateClaimsScopesStore>().FirstOrDefault();

            privateStore.AddPrivateScopes("Bjorn",new string[]{"flames"});
            privateStore.AddPrivateClaims("Bjorn", new string[] { "bullet" });

            var fullClientStore = P7.Core.Global.ServiceProvider.GetServices<IFullClientStore>().FirstOrDefault();

            await fullClientStore.InsertClientAsync(new Client
            {
                ClientId = "client",
                AllowedGrantTypes = GrantTypes.ClientCredentials,

                ClientSecrets =
                {
                    new Secret("secret".Sha256())
                },
                AllowedScopes = { "arbitrary" }
            });

            await fullClientStore.InsertClientAsync(new Client
            {
                ClientId = "resource-owner-client",
                AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
                AllowOfflineAccess = true,
                RefreshTokenUsage = TokenUsage.OneTimeOnly,
                ClientSecrets =
                {
                    new Secret("secret".Sha256())
                },
                AllowedScopes = { "arbitrary" }
            });



            await fullClientStore.InsertClientAsync(new Client
            {
                ClientId = "public-resource-owner-client",
                AllowedGrantTypes = new List<string> { "public_refresh_token" },
                RequireClientSecret = false,
                AllowedScopes = { "arbitrary" }
            });


            var apiResourceList = new List<ApiResource>
            {
                new ApiResource("arbitrary", "Arbitrary Scope")
                {
                    ApiSecrets =
                    {
                        new Secret("secret".Sha256())
                    }
                }
            };

            var resourceStore = P7.Core.Global.ServiceProvider.GetServices<IResourceStore>().FirstOrDefault();
            var adminResourceStore = P7.Core.Global.ServiceProvider.GetServices<IAdminResourceStore>().FirstOrDefault();

            foreach (var apiResource in apiResourceList)
            {
                await adminResourceStore.ApiResourceStore.InsertApiResourceAsync(apiResource);
            }

            var dd = await adminResourceStore.ApiResourceStore.PageAsync(10, null);
        }
        private static void ConfigureTagHelperBase(IHostingEnvironment env)
        {
            var version = typeof(Startup).GetTypeInfo()
                .Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;
            if (env.EnvironmentName == "Development")
            {
                version += "." + Guid.NewGuid().ToString().GetHashCode();
            }
            P7TagHelperBase.Version = version;
        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
            IHostingEnvironment env,
            ILoggerFactory loggerFactory,
            IApplicationLifetime appLifetime)
        {
            app.UseMiddleware<Convert302ResponseMiddleware>();
            app.UseIpRateLimiting();

         //   LoadGraphQLAuthority();
            LoadIdentityServer4Data();

            var supportedCultures = new List<CultureInfo>
            {
                new CultureInfo("en-US"),
                new CultureInfo("en-AU"),
                new CultureInfo("en-GB"),
                new CultureInfo("es-ES"),
                new CultureInfo("ja-JP"),
                new CultureInfo("fr-FR"),
                new CultureInfo("zh"),
                new CultureInfo("zh-CN")
            };
            var options = new RequestLocalizationOptions
            {
                //     RequestCultureProviders = new List<IRequestCultureProvider>(),
                DefaultRequestCulture = new RequestCulture("en-US"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };
            app.UseRequestLocalization(options);

            ConfigureTagHelperBase(env);

            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();
            // Add Serilog to the logging pipeline
            loggerFactory.AddSerilog();
            // Ensure any buffered events are sent at shutdown
            appLifetime.ApplicationStopped.Register(Log.CloseAndFlush);



            app.AddAllConfigureRegistrants();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();
            var contentTypeProvider = new FileExtensionContentTypeProvider();

            contentTypeProvider.Mappings.Add(".tag", "text/plain");

            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new CbvPhysicalFileProvider(env.WebRootPath),
                RequestPath = new PathString("/cb-v"),
                ServeUnknownFileTypes = true,
                ContentTypeProvider = contentTypeProvider
            });

            var root = env.ContentRootFileProvider;
            var rewriteOptions = new MutableRewriteOptions()
                .AddIISUrlRewrite(root, "IISUrlRewrite.config");
            P7.Core.Global.ArbitraryObjects.Add("rewrite-optons", (object)rewriteOptions);
            app.UseP7Rewriter((RewriteOptions)P7.Core.Global.ArbitraryObjects["rewrite-optons"]);

            //enable session before MVC
            //=========================  
            app.UseSession(new SessionOptions
                {
                    CookieSecure = CookieSecurePolicy.SameAsRequest
                }
            );

            app.UseAuthentication();
            app.UsePublicRefreshToken();
            app.UseIdentityServer();

            app.UseCors("CorsPolicy");

            app.UseMvc(routes =>
            {
                routes.MapRoute("areaRoute", "{area:exists}/{controller=Admin}/{action=Index}/{id?}");
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
