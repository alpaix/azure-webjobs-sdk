using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class SingletonEndToEndTests : IClassFixture<SingletonEndToEndTests.TestFixture>
    {
        private const string TestArtifactsPrefix = "singletone2e";
        private const string Queue1Name = TestArtifactsPrefix + "q1%rnd%";
        private const string Queue2Name = TestArtifactsPrefix + "q2%rnd%";

        private static RandomNameResolver _resolver = new RandomNameResolver();

        public SingletonEndToEndTests()
        {
            TestJobs.Reset();
        }

        private JobHost CreateTestJobHost(int hostId)
        {
            TestJobActivator activator = new TestJobActivator(hostId);

            JobHostConfiguration config = new JobHostConfiguration
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(typeof(TestJobs)),
                JobActivator = activator,
            };
            config.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);

            JobHost host = new JobHost(config);

            return host;
        }

        [Fact]
        public async Task SingletonFunction_MultipleConcurrentInvocations_InvocationsAreSerialized()
        {
            JobHost host = CreateTestJobHost(1);
            host.Start();

            // make a bunch of parallel invocations
            int numInvocations = 20;
            List<Task> invokeTasks = new List<Task>();
            for (int i = 0; i < numInvocations; i++)
            {
                WorkItem workItem = new WorkItem
                {
                    ID = i + 1,
                    Category = 3,
                    Description = "Work Item " + i
                };
                invokeTasks.Add(host.CallAsync(typeof(TestJobs).GetMethod("SingletonJob"), new { workItem = workItem }));
            }
            await Task.WhenAll(invokeTasks.ToArray());

            Assert.Equal(numInvocations, TestJobs.JobInvocations[1]);

            host.Stop();
            host.Dispose();
        }

        [Fact]
        public async Task SingletonTriggerFunction_MultipleHosts_TriggerMode_OnlyOneHostRunsTrigger()
        {
            // create and start multiple hosts concurrently
            int numHosts = 3;
            List<JobHost> hosts = new List<JobHost>();
            for (int i = 0; i < numHosts; i++)
            {
                JobHost host = CreateTestJobHost(i);
                host.Start();
                hosts.Add(host);
            }

            // write a bunch of messages
            int numMessages = 20;
            for (int i = 0; i < numMessages; i++)
            {
                JObject workItem = new JObject
                {
                    { "ID", i + 1 },
                    { "Category", 3 },
                    { "Description", "Test Work Item " + i }
                };
                await hosts[0].CallAsync(typeof(TestJobs).GetMethod("EnqueueQueue1TestMessage"), new { message = workItem.ToString() });
            }

            // wait for all the messages to be processed by the job
            await TestHelpers.Await(() =>
                {
                    return TestJobs.Queue1MessageCount == numMessages &&
                           TestJobs.JobInvocations.Select(p => p.Value).Sum() == numMessages;
                }, pollingInterval: 500);

            // verify that only a single host was actively processing the messages
            int[] hostIds = TestJobs.JobInvocations.Where(p => p.Value > 0).Select(p => p.Key).ToArray();
            Assert.Equal(1, hostIds.Length);
            int activeHostId = hostIds.Single();
            Assert.Equal(numMessages, TestJobs.JobInvocations[activeHostId]);

            foreach (JobHost host in hosts)
            {
                host.Stop();
                host.Dispose();
            }
        }

        [Fact]
        public async Task SingletonTriggerFunction_FunctionMode()
        {
            JobHost host = CreateTestJobHost(1);
            host.Start();

            // make a bunch of parallel invocations
            int numMessages = 20;
            List<Task> invokeTasks = new List<Task>();
            JsonSerializer serializer = new JsonSerializer();
            for (int i = 0; i < numMessages; i++)
            {
                JObject workItem = new JObject
                {
                    { "ID", i + 1 },
                    { "Category", 3 },
                    { "Description", "Test Work Item " + i }
                };
                invokeTasks.Add(host.CallAsync(typeof(TestJobs).GetMethod("EnqueueQueue2TestMessage"), new { message = workItem.ToString() }));
            }
            await Task.WhenAll(invokeTasks.ToArray());

            // wait for all the messages to be processed by the job
            await TestHelpers.Await(() =>
            {
                return TestJobs.Queue2MessageCount == numMessages &&
                       TestJobs.JobInvocations.Select(p => p.Value).Sum() == numMessages;
            }, pollingInterval: 500);

            Assert.Equal(numMessages, TestJobs.JobInvocations[1]);

            host.Stop();
            host.Dispose();
        }

        public class WorkItem
        {
            public int ID { get; set; }
            public int Category { get; set; }
            public string Description { get; set; }
        }

        public class TestJobs
        {
            public static int Queue1MessageCount = 0;
            public static int Queue2MessageCount = 0;
            public static Dictionary<int, int> JobInvocations = new Dictionary<int, int>();
            private static object syncLock = new object();
            private static bool isLocked = false;

            private readonly int _hostId;

            public TestJobs(int hostId)
            {
                _hostId = hostId;
            }

            [Singleton(SingletonMode.Trigger)]
            public async Task SingletonTriggerJob_TriggerMode([QueueTrigger(Queue1Name)] WorkItem workItem)
            {
                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            [Singleton(SingletonMode.Function)]
            public async Task SingletonTriggerJob_FunctionMode([QueueTrigger(Queue2Name)] WorkItem workItem)
            {
                await Task.Delay(50);
                IncrementJobInvocationCount();
            }

            [Singleton(SingletonMode.Function)]
            [NoAutomaticTrigger]
            public async Task SingletonJob(WorkItem workItem)
            {
                // When run concurrently, this job will fail very reliably
                if (isLocked)
                {
                    throw new Exception("Error!");
                }
                isLocked = true;

                await Task.Delay(50);
                IncrementJobInvocationCount();

                isLocked = false;
            }

            [NoAutomaticTrigger]
            public void EnqueueQueue1TestMessage(string message,
                [Queue(Queue1Name)] ICollector<string> queueMessages)
            {
                queueMessages.Add(message);
                Interlocked.Increment(ref Queue1MessageCount);
            }

            [NoAutomaticTrigger]
            public void EnqueueQueue2TestMessage(string message,
                [Queue(Queue2Name)] ICollector<string> queueMessages)
            {
                queueMessages.Add(message);
                Interlocked.Increment(ref Queue2MessageCount);
            }

            public static void Reset()
            {
                Queue1MessageCount = 0;
                Queue2MessageCount = 0;
                JobInvocations = new Dictionary<int, int>();
                isLocked = false;
            }

            private void IncrementJobInvocationCount()
            {
                lock (syncLock)
                {
                    if (!JobInvocations.ContainsKey(_hostId))
                    {
                        JobInvocations[_hostId] = 0;
                    }
                    JobInvocations[_hostId]++;
                }
            }
        }

        private class TestJobActivator : IJobActivator
        {
            private int _hostId;

            public TestJobActivator(int hostId)
            {
                _hostId = hostId;
            }

            public T CreateInstance<T>()
            {
                return (T)Activator.CreateInstance(typeof(T), _hostId);
            }
        }

        public class Info
        {
            public string Tag { get; set; }
        }

        private class TestFixture : IDisposable
        {
            private CloudStorageAccount storageAccount;

            public TestFixture()
            {
                JobHostConfiguration config = new JobHostConfiguration();
                storageAccount = CloudStorageAccount.Parse(config.StorageConnectionString);
            }

            public void Dispose()
            {
                if (storageAccount != null)
                {
                    CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
                    foreach (var queue in queueClient.ListQueues(TestArtifactsPrefix))
                    {
                        queue.Delete();
                    }
                }
            }
        }
    }

    
}
