// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BasicTestApp;
using Castle.DynamicProxy.Contributors;
using Microsoft.AspNetCore.Components.E2ETest.Infrastructure;
using Microsoft.AspNetCore.Components.E2ETest.Infrastructure.ServerFixtures;
using Microsoft.AspNetCore.Components.E2ETest.ServerExecutionTests;
using Microsoft.AspNetCore.E2ETesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Components.E2ETests.ServerExecutionTests
{
    public class CircuitGracefulTerminationTests : BasicTestAppTestBase, IDisposable
    {
        public CircuitGracefulTerminationTests(
            BrowserFixture browserFixture,
            ToggleExecutionModeServerFixture<Program> serverFixture,
            ITestOutputHelper output)
            : base(browserFixture, serverFixture.WithServerExecution(), output)
        {
        }

        public TaskCompletionSource<object> GracefulDisconnectCompletionSource { get; private set; }
        public TestSink Sink { get; private set; }
        public List<(Extensions.Logging.LogLevel level, string eventIdName)> Messages { get; private set; }

        public override async Task InitializeAsync()
        {
            // These tests manipulate the browser in  ways that make it impossible to use the same browser
            // instance across tests (One of the tests closes the browser). For that reason we simply create
            // a new browser instance for every test in this class sos that there are no issues when running
            // them together.
            await base.InitializeAsync(Guid.NewGuid().ToString());
        }

        protected override void InitializeAsyncCore()
        {
            Navigate(ServerPathBase, noReload: false);
            MountTestComponent<CounterComponent>();
            Browser.Equal("Current count: 0", () => Browser.FindElement(By.TagName("p")).Text);

            GracefulDisconnectCompletionSource = new TaskCompletionSource<object>(TaskContinuationOptions.RunContinuationsAsynchronously);
            Sink = _serverFixture.Host.Services.GetRequiredService<TestSink>();
            Messages = new List<(Extensions.Logging.LogLevel level, string eventIdName)>();
            Sink.MessageLogged += Log;
        }

        [Fact]
        public async Task ReloadingThePage_GracefullyDisconnects_TheCurrentCircuit()
        {
            // Arrange & Act
            _ = ((IJavaScriptExecutor)Browser).ExecuteScript("location.reload()");
            await Task.WhenAny(Task.Delay(10000), GracefulDisconnectCompletionSource.Task);

            // Assert
            Assert.Contains((Extensions.Logging.LogLevel.Debug, "CircuitTerminatedGracefully"), Messages);
            Assert.Contains((Extensions.Logging.LogLevel.Debug, "CircuitDisconnectedPermanently"), Messages);
        }

        [Fact]
        public async Task ClosingTheBrowserWindow_GracefullyDisconnects_TheCurrentCircuit()
        {
            // Arrange & Act
            Browser.Close();
            // Set to null so that other tests in this class can create a new browser if necessary so
            // that tests don't fail when running together.
            Browser = null;

            await Task.WhenAny(Task.Delay(10000), GracefulDisconnectCompletionSource.Task);

            // Assert
            Assert.Contains((Extensions.Logging.LogLevel.Debug, "CircuitTerminatedGracefully"), Messages);
            Assert.Contains((Extensions.Logging.LogLevel.Debug, "CircuitDisconnectedPermanently"), Messages);
        }

        [Fact]
        public async Task ClosingTheBrowserWindow_GracefullyDisconnects_WhenNavigatingAwayFromThePage()
        {
            // Arrange & Act
            Browser.Navigate().GoToUrl("about:blank");
            await Task.WhenAny(Task.Delay(10000), GracefulDisconnectCompletionSource.Task);

            // Assert
            Assert.Contains((Extensions.Logging.LogLevel.Debug, "CircuitTerminatedGracefully"), Messages);
            Assert.Contains((Extensions.Logging.LogLevel.Debug, "CircuitDisconnectedPermanently"), Messages);
        }

        private void Log(WriteContext wc)
        {
            if ((Extensions.Logging.LogLevel.Debug, "CircuitTerminatedGracefully") == (wc.LogLevel, wc.EventId.Name))
            {
                GracefulDisconnectCompletionSource.TrySetResult(null);
            }
            Messages.Add((wc.LogLevel, wc.EventId.Name));
        }

        public void Dispose()
        {
            if (Sink != null)
            {
                Sink.MessageLogged -= Log;
            }
        }
    }
}
