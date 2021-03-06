﻿using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using WcfLib.Client;
using WcfLib.Test.Service;
using WcfLib.Test.TestFramework;

namespace WcfLib.Test.Client
{
    /// <summary>
    ///     Test the WCF client with an actual WCF service in different scenarios
    /// </summary>
    [TestClass]
    public class WcfClientIntegrationTest
    {
        private ChannelFactory<IMockService> _channelFactory;
        private ServiceHost _host;
        private Mock<WcfChannelPool<IMockService>> _wcfChannelPoolMock;
        private WcfClient<IMockService> _wcfClient;

        [TestInitialize]
        public void Setup()
        {
            StartWcfService();

            var clientBinding = new NetTcpBinding(SecurityMode.None);
            clientBinding.SendTimeout = TimeSpan.FromSeconds(3);
            _channelFactory = new ChannelFactory<IMockService>(clientBinding, "net.tcp://localhost:20001");

            _wcfChannelPoolMock = new Mock<WcfChannelPool<IMockService>>(_channelFactory);
            _wcfChannelPoolMock.CallBase = true;
            _wcfClient = new WcfClient<IMockService>(_wcfChannelPoolMock.Object, new NoRetryPolicy());
        }

        private void StartWcfService()
        {
            _host = new ServiceHost(typeof (MockService));
            var serverBinding = new NetTcpBinding(SecurityMode.None);
            _host.AddServiceEndpoint(typeof (IMockService), serverBinding, "net.tcp://0.0.0.0:20001");
            _host.Open();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _host.Close();
        }

        [TestMethod]
        public async Task SingleCall()
        {
            int r = await _wcfClient.Call(s => s.EchoInt(42));

            Assert.AreEqual(42, r);
            _wcfChannelPoolMock.Verify(m => m.GetChannel(), Times.Exactly(1));
            _wcfChannelPoolMock.Verify(m => m.ReleaseChannel(MoqExtensions.AnyObject<IClientChannel>()),
                Times.Exactly(1));
        }

        [TestMethod]
        public async Task SequentialSeriesOfCalls()
        {
            const int count = 100;
            for (int i = 0; i < count; i++)
            {
                int req = i;
                int rep = await _wcfClient.Call(s => s.EchoInt(req));
                Assert.AreEqual(i, rep);
            }

            _wcfChannelPoolMock.Verify(m => m.GetChannel(), Times.Exactly(count));
            _wcfChannelPoolMock.Verify(m => m.ReleaseChannel(MoqExtensions.AnyObject<IClientChannel>()),
                Times.Exactly(count));
        }

        [TestMethod]
        [Ignore]
        public async Task UserCodeFailureDoesntRecycleChannel()
        {
            //Make a normal call,
            //Make a call which generates a user code exception
            //Make another normal call 
            await _wcfClient.Call(s => s.EchoInt(1));
            await AssertEx.Throws<ApplicationException>(() => _wcfClient.Call(s => s.Fail()));
            await _wcfClient.Call(s => s.EchoInt(2));

            //Verify that the channel was released to the pool 3 times
            //This means that WcfClient didn't recycle the channel after user code failure
            _wcfChannelPoolMock.Verify(m => m.GetChannel(), Times.Exactly(3));
            _wcfChannelPoolMock.Verify(m => m.ReleaseChannel(MoqExtensions.AnyObject<IClientChannel>()),
                Times.Exactly(3));
        }

        [TestMethod]
        public async Task CommunicationFailureRecyclesChannel()
        {
            //Make a call,
            //Restart the WCF service to break the existing channel
            //Make a call, expect it to fail and recycle the channel
            //Make a call, expect it to succeed
            await _wcfClient.Call(s => s.EchoInt(1));
            _host.Close();
            StartWcfService();
            await AssertEx.Throws<CommunicationException>(() => _wcfClient.Call(s => s.EchoInt(2)));
            await _wcfClient.Call(s => s.EchoInt(3));

            //Verify that the channel was released to the pool only twice
            _wcfChannelPoolMock.Verify(m => m.GetChannel(), Times.Exactly(3));
            _wcfChannelPoolMock.Verify(m => m.ReleaseChannel(MoqExtensions.AnyObject<IClientChannel>()),
                Times.Exactly(2));
        }

        [TestMethod]
        public async Task OpenBadAddress()
        {
            var clientBinding = new NetTcpBinding(SecurityMode.None);
            var channelFactory = new ChannelFactory<IMockService>(clientBinding, "net.tcp://localhost:20002");
            var wcfClient = new WcfClient<IMockService>(new WcfChannelPool<IMockService>(channelFactory), new NoRetryPolicy());
            await AssertEx.Throws<EndpointNotFoundException>(async () => await wcfClient.Call(s => s.EchoInt(42)));
        }

        [TestMethod]
        public async Task MultithreadedStressTest()
        {
            const int maxParrallism = 50;
            var action = new Func<int, Task>(async x => 
            {
                var rep = await _wcfClient.Call(s => s.EchoInt(x));
                Assert.AreEqual(x,rep);
            });

            ActionBlock<int> actionBlock = new ActionBlock<int>(action, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxParrallism });
            const int count = 10000;
            for (int i = 0; i < count; i++)
            {
                actionBlock.Post(i);
            }
            actionBlock.Complete();
            await actionBlock.Completion;
            Assert.AreEqual(maxParrallism, _wcfChannelPoolMock.Object.PoolSize);
        }
    }
}