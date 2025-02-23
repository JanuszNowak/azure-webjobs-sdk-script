﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    // Tests for configuration that lives under the "Logging" section of the IConfiguration
    public class LoggingConfigurationTests
    {
        private readonly string _loggingPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging");

        [Fact]
        public void Logging_Binds_AppInsightsOptions()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" },
                        { "APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=some_other_key" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SamplingSettings", "IsEnabled"), "false" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SnapshotConfiguration", "IsEnabled"), "false" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                ApplicationInsightsLoggerOptions appInsightsOptions = host.Services.GetService<IOptions<ApplicationInsightsLoggerOptions>>().Value;

                Assert.Equal("some_key", appInsightsOptions.InstrumentationKey);
                Assert.Equal("InstrumentationKey=some_other_key", appInsightsOptions.ConnectionString);
                Assert.Null(appInsightsOptions.SamplingSettings);
                Assert.False(appInsightsOptions.SnapshotConfiguration.IsEnabled);
            }
        }

        [Fact]
        public void Logging_Filters()
        {
            // Ensure that the logging path is configured for filter configuration binding in .NET Core.
            // All of the filtering details are handled by the built-in logging infrastructure, so here we
            // simply want to make sure that we're populating the LoggerFilterOptions from our config.

            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(_loggingPath, "LogLevel", "Default"), "Error" },
                        { ConfigurationPath.Combine(_loggingPath, "LogLevel", "Some.Custom.Category"), "Trace" },
                        { ConfigurationPath.Combine(_loggingPath, "Console", "LogLevel", "Default"), "Trace" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                LoggerFilterOptions filterOptions = host.Services.GetService<IOptions<LoggerFilterOptions>>().Value;

                Assert.Equal(6, filterOptions.Rules.Count);

                var rules = filterOptions.Rules.ToArray();

                var rule = rules[0];
                Assert.Null(rule.ProviderName);
                Assert.Null(rule.CategoryName);
                Assert.Null(rule.LogLevel);
                Assert.NotNull(rule.Filter); // The broad "allowed category" filter.

                rule = rules[1];
                Assert.Equal(LogLevel.Trace, rule.LogLevel);
                Assert.Equal("Console", rule.ProviderName);

                rule = rules[2];
                Assert.Equal(LogLevel.Trace, rule.LogLevel);
                Assert.Null(rule.ProviderName);
                Assert.Equal("Some.Custom.Category", rule.CategoryName);

                rule = rules[3];
                Assert.Equal(LogLevel.Error, rule.LogLevel);
                Assert.Null(rule.ProviderName);

                rule = rules[4];
                Assert.Equal(typeof(SystemLoggerProvider).FullName, rule.ProviderName);
                Assert.Null(rule.CategoryName);
                Assert.Equal(LogLevel.None, rule.LogLevel);
                Assert.Null(rule.Filter);

                rule = rules[5];
                Assert.Equal(typeof(SystemLoggerProvider).FullName, rule.ProviderName);
                Assert.Null(rule.CategoryName);
                Assert.Null(rule.LogLevel);
                Assert.NotNull(rule.Filter); // The system-specific "allowed category" filter
            }
        }

        [Fact]
        public void Logging_DefaultsToInformation()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                LoggerFilterOptions filterOptions = host.Services.GetService<IOptions<LoggerFilterOptions>>().Value;

                Assert.Equal(LogLevel.None, filterOptions.MinLevel);

                var rules = filterOptions.Rules.ToArray();
                Assert.Equal(3, rules.Length);

                var rule = rules[0];
                Assert.Null(rule.ProviderName);
                Assert.Null(rule.CategoryName);
                Assert.Null(rule.LogLevel);
                Assert.NotNull(rule.Filter); // The broad "allowed category" filter.

                rule = rules[1];
                Assert.Equal(typeof(SystemLoggerProvider).FullName, rule.ProviderName);
                Assert.Null(rule.CategoryName);
                Assert.Equal(LogLevel.None, rule.LogLevel);
                Assert.Null(rule.Filter);

                rule = rules[2];
                Assert.Equal(typeof(SystemLoggerProvider).FullName, rule.ProviderName);
                Assert.Null(rule.CategoryName);
                Assert.Null(rule.LogLevel);
                Assert.NotNull(rule.Filter); // The system-specific "allowed category" filter
            }
        }

        [Fact]
        public void Initialize_AppliesLoggerConfig()
        {
            // Default mininum level is Information, so set this to Warning and make
            // sure Information-level logs are filtered

            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(_loggingPath, "LogLevel", "Default"), "Warning" },
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging(l =>
                {
                    l.AddProvider(loggerProvider);
                });

            using (IHost host = hostBuilder.Build())
            {
                ILogger logger = host.Services.GetService<ILogger<LoggingConfigurationTests>>();

                logger.LogInformation("Information");
                logger.LogWarning("Warning");

                LogMessage messages = loggerProvider.GetAllLogMessages().Single();
                Assert.Equal("Warning", messages.FormattedMessage);
            }
        }

        [Fact]
        public void LoggerProviders_Default()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

                Assert.Equal(5, loggerProviders.Count());
                loggerProviders.OfType<SystemLoggerProvider>().Single();
                loggerProviders.OfType<HostFileLoggerProvider>().Single();
                loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
                loggerProviders.OfType<AzureMonitorDiagnosticLoggerProvider>().Single();
            }
        }

        [Fact]
        public void LoggerProviders_ConsoleEnabled_IfDevelopmentEnvironment()
        {
            var hostBuilder = new HostBuilder()
                .UseEnvironment(EnvironmentName.Development)
                .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

                Assert.Equal(6, loggerProviders.Count());
                loggerProviders.OfType<SystemLoggerProvider>().Single();
                loggerProviders.OfType<HostFileLoggerProvider>().Single();
                loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
                loggerProviders.OfType<ConsoleLoggerProvider>().Single();
                loggerProviders.OfType<AzureMonitorDiagnosticLoggerProvider>().Single();
            }
        }

        [Fact]
        public void LoggerProviders_ConsoleEnabled_InConfiguration()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(_loggingPath, "Console", "IsEnabled"), "True" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

                Assert.Equal(6, loggerProviders.Count());
                loggerProviders.OfType<SystemLoggerProvider>().Single();
                loggerProviders.OfType<HostFileLoggerProvider>().Single();
                loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
                loggerProviders.OfType<ConsoleLoggerProvider>().Single();
                loggerProviders.OfType<AzureMonitorDiagnosticLoggerProvider>().Single();
            }
        }

        [Fact]
        public void LoggerProviders_ApplicationInsights()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" }
                });
                })
            .ConfigureDefaultTestWebScriptHost();

            using (IHost host = hostBuilder.Build())
            {
                IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

                Assert.Equal(6, loggerProviders.Count());
                loggerProviders.OfType<SystemLoggerProvider>().Single();
                loggerProviders.OfType<HostFileLoggerProvider>().Single();
                loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
                loggerProviders.OfType<ApplicationInsightsLoggerProvider>().Single();
                loggerProviders.OfType<AzureMonitorDiagnosticLoggerProvider>().Single();
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Foo,Bar")]
        [InlineData("Foo,FunctionAppLogs,Bar")]
        public void LoggerProviders_AzureMonitor(string azureMonitorEnabledValue)
        {
            bool isAzureMonitorEnabled = false;

            var hostBuilder = new HostBuilder()
            .ConfigureDefaultTestWebScriptHost()
            .ConfigureServices(s =>
            {
                TestEnvironment environment = new TestEnvironment();
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName, "something.azurewebsites.net");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureMonitorCategories, azureMonitorEnabledValue);
                s.AddSingleton<IEnvironment>(environment);

                isAzureMonitorEnabled = environment.IsAzureMonitorEnabled();
            });

            using (IHost host = hostBuilder.Build())
            {
                IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

                int expectedCount = isAzureMonitorEnabled ? 5 : 4;

                Assert.Equal(5, loggerProviders.Count());
                loggerProviders.OfType<SystemLoggerProvider>().Single();
                loggerProviders.OfType<HostFileLoggerProvider>().Single();
                loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
                if (isAzureMonitorEnabled)
                {
                    loggerProviders.OfType<AzureMonitorDiagnosticLoggerProvider>().Single();
                }
            }
        }
    }
}