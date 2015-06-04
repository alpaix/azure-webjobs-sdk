// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host
{
    public class SingletonManagerTests : IClassFixture<SingletonManagerTests.TestFixture>
    {
        private const string TestLockId = @"Program.TestFunction/TestScope";

        private SingletonManager _singletonManager;

        public SingletonManagerTests(TestFixture fixture)
        {
            _singletonManager = fixture.SingletonManager;
        }

        [Fact]
        public async Task TryLockAsync_AquiresLock()
        {
            CancellationToken cancellationToken = CancellationToken.None;

            TimeSpan leasePeriod = TimeSpan.FromMinutes(1);
            object firstLock = await _singletonManager.TryLockAsync(TestLockId, cancellationToken, leasePeriod: leasePeriod);
            Assert.NotNull(firstLock);

            // try to acquire the lock again - expect this to fail
            object secondLock = await _singletonManager.TryLockAsync(TestLockId, cancellationToken);
            Assert.Null(secondLock);

            await _singletonManager.ReleaseLockAsync(firstLock, cancellationToken);

            secondLock = await _singletonManager.TryLockAsync(TestLockId, cancellationToken);
            Assert.NotNull(secondLock);
        }

        public class TestFixture
        {
            public TestFixture()
            {
                CreateLockManager().Wait();
            }

            public async Task CreateLockManager()
            {
                DefaultStorageAccountProvider accountProvider = new DefaultStorageAccountProvider();
                IStorageAccount account = await accountProvider.GetStorageAccountAsync(CancellationToken.None);
                IStorageBlobClient client = account.CreateBlobClient();
                SingletonManager = new SingletonManager(client);
            }

            internal SingletonManager SingletonManager { get; private set; }
        }
    }
}
