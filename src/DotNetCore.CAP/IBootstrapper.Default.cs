﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetCore.CAP
{
    /// <summary>
    /// Default implement of <see cref="IBootstrapper"/>.
    /// </summary>
    internal class DefaultBootstrapper : IBootstrapper
    {
        private readonly ILogger<DefaultBootstrapper> _logger;
        private readonly IApplicationLifetime _appLifetime;
        private readonly CancellationTokenSource _cts;
        private readonly CancellationTokenRegistration _ctsRegistration;
        private Task _bootstrappingTask;

        public DefaultBootstrapper(
            ILogger<DefaultBootstrapper> logger,
            IOptions<CapOptions> options,
            IStorage storage,
            IApplicationLifetime appLifetime,
            IEnumerable<IProcessingServer> servers)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            Options = options.Value;
            Storage = storage;
            Servers = servers;

            _cts = new CancellationTokenSource();
            _ctsRegistration = appLifetime.ApplicationStopping.Register(() =>
            {
                _cts.Cancel();
                try
                {
                    _bootstrappingTask?.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException ex)
                {
                    _logger.ExpectedOperationCanceledException(ex);
                }
            });
        }

        protected CapOptions Options { get; }

        protected IStorage Storage { get; }

        protected IEnumerable<IProcessingServer> Servers { get; }

        public Task BootstrapAsync()
        {
            return (_bootstrappingTask = BootstrapTaskAsync());
        }

        private async Task BootstrapTaskAsync()
        {
            await Storage.InitializeAsync(_cts.Token);

            if (_cts.IsCancellationRequested) return;

            await BootstrapCoreAsync();

            if (_cts.IsCancellationRequested) return;

            foreach (var item in Servers)
            {
                try
                {
                    item.Start();
                }
                catch (Exception ex)
                {
                    _logger.ServerStartedError(ex);
                }
            }

            _ctsRegistration.Dispose();
            _cts.Dispose();
        }

        public virtual Task BootstrapCoreAsync()
        {
            _appLifetime.ApplicationStopping.Register(() =>
            {
                foreach (var item in Servers)
                {
                    item.Dispose();
                }
            });
            return Task.CompletedTask;
        }
    }
}