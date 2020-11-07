﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plato.Messaging.RMQ;
using Plato.Messaging.RMQ.Builder;
using Plato.WebTestHarness.RMQConsumers;
using System;

namespace Plato.WebTestHarness
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }                

        public void ConfigureServices(IServiceCollection services)
        {
            var configManager = new RMQConfigurationManager();
            var connectionSettings = configManager.GetConnectionSettings("connection");
            var exchangeSettings = configManager.GetExchangeSettings("my_rmq_test_exchange");
            var queueSettings = configManager.GetQueueSettings("my_rmq_test");

            services.AddRMQBoundConsumer<TestBoundConsumerText>(options =>
            {
                options.ConnectionSettings = connectionSettings;
                options.QueueSettings = queueSettings;
            });

            //services.AddRMQBoundSubscriber<TestBoundConsumerText>(options =>
            //{
            //    options.ConnectionSettings = connectionSettings;
            //    options.ExchangeSettings = exchangeSettings;
            //    options.QueueSettings = queueSettings;                               
            //});

            services.AddControllers();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseRMQBoundConsumers((ex) => Console.WriteLine(ex));
        }
    }
}
