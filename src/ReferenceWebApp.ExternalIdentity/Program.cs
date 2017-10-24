﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ReferenceWebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseHealthChecks("/health", TimeSpan.FromSeconds(3))     // Or to host on a separate port: .UseHealthChecks(port)
                .UseIISIntegration()
                .UseSetting("detailedErrors", "true")
                .CaptureStartupErrors(true)
                .UseStartup<Startup>()
                .UseApplicationInsights()
                .Build();
    }
}
