﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using WcfLib.Client;
using WcfLib.Test.Service;

namespace WcfLib.Test.Performance
{
    [TestClass]
    public class PerformanceTest
    {
        private ServiceHost _host;
        private const int iterationCount = 10000;
        [TestInitialize]
        public void Setup()
        {
            _host = new ServiceHost(typeof(MockService));
            _host.AddServiceEndpoint(typeof(IMockService), new NetTcpBinding(SecurityMode.None), "net.tcp://0.0.0.0:20001");
            _host.AddServiceEndpoint(typeof(IMockService), new NetTcpBinding(SecurityMode.Transport), "net.tcp://0.0.0.0:20002");
            _host.Open();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _host.Close();
        }

        [TestMethod]
        public async Task WcfClientNoSecurity()
        {
            var clientBinding = new NetTcpBinding(SecurityMode.None);
            var channelFactory = new ChannelFactory<IMockService>(clientBinding, "net.tcp://localhost:20001");
            WcfClient<IMockService> client = new WcfClient<IMockService>(channelFactory);
            await Measure(async () =>
            {
                await client.Call(s => s.Echo(1));
            });

        }

        [TestMethod]
        public async Task WcfClientTransportSecurity()
        {
            var clientBinding = new NetTcpBinding(SecurityMode.Transport);
            var channelFactory = new ChannelFactory<IMockService>(clientBinding, "net.tcp://localhost:20002");
            WcfClient<IMockService> client = new WcfClient<IMockService>(channelFactory);
            await Measure(async () =>
            {
                await client.Call(s => s.Echo(1));
            });

        }

        [TestMethod]
        public async Task CachedChannelFactoryNonCachedChannelNoSecurity()
        {
            var clientBinding = new NetTcpBinding(SecurityMode.None);
            var channelFactory = new ChannelFactory<IMockService>(clientBinding, "net.tcp://localhost:20001");

            await Measure(async () =>
            {
                var proxy = channelFactory.CreateChannel();
                var channel = (IServiceChannel) proxy;
                await proxy.Echo(1);
                channel.Close();
            });

        }

        [TestMethod]
        public async Task CachedChannelFactoryNonCachedChannelTransportSecurity()
        {
            var clientBinding = new NetTcpBinding(SecurityMode.Transport);
            var channelFactory = new ChannelFactory<IMockService>(clientBinding, "net.tcp://localhost:20002");

            await Measure(async () =>
            {
                var proxy = channelFactory.CreateChannel();
                var channel = (IServiceChannel)proxy;
                await proxy.Echo(1);
                channel.Close();
            });
        }
        
        [TestMethod]
        public async Task AllTests()
        {
            await WcfClientNoSecurity();
            await WcfClientTransportSecurity();
            await CachedChannelFactoryNonCachedChannelNoSecurity();
            await CachedChannelFactoryNonCachedChannelTransportSecurity();
        }

        async Task Measure(Func<Task> action, [CallerMemberName]string name = null)
        {
            //Make one warmup call
            action();

            List<double> latencies = new List<double>(iterationCount);

            Stopwatch sw = new Stopwatch();
            for (int i = 0; i < iterationCount; i++)
            {
                sw.Restart();
                await action();
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            latencies.Sort();
            double average = latencies.Average();
            double p99 = latencies[(int) (latencies.Count*0.99)];

            Console.WriteLine("{0}: Average: {1:0.000}, 99%: {2:0.000}", name, average, p99);
        }


    }
}
