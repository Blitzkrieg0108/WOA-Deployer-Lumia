﻿using System.IO;
using System.Threading.Tasks;
using Deployer.Execution;
using Deployer.FileSystem;
using Deployer.Services;

namespace Deployer.Lumia.Tasks
{
    [TaskDescription("Configuring BCD")]
    public class ConfigureBCD : IDeploymentTask
    {
        private readonly IPhone phone;
        private readonly IBcdInvokerFactory bcdInvokerFactory;

        public ConfigureBCD(IPhone phone, IBcdInvokerFactory bcdInvokerFactory)
        {
            this.phone = phone;
            this.bcdInvokerFactory = bcdInvokerFactory;
        }

        public async Task Execute()
        {
            var efiEsp = await phone.GetPartitionByName(PartitionName.EfiEsp);

            var bcdPath = efiEsp.Root.CombineRelativeBcdPath();
            var bcdInvoker = bcdInvokerFactory.Create(bcdPath);
            await new BcdConfigurator(bcdInvoker, efiEsp).SetupBcd();                       
        }
    }
}