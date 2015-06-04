// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal static class FunctionIndexerFactory
    {
        public static FunctionIndexer Create(CloudStorageAccount account = null, INameResolver nameResolver = null, IExtensionRegistry extensionRegistry = null)
        {
            IStorageAccount storageAccount = account != null ? new StorageAccount(account) : null;
            IStorageAccountProvider storageAccountProvider = new SimpleStorageAccountProvider
            {
                StorageAccount = account
            };
            IExtensionTypeLocator extensionTypeLocator = new NullExtensionTypeLocator();
            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor =
                new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor =
                new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, extensionTypeLocator,
                new FixedHostIdProvider("test"), new SimpleQueueConfiguration(maxDequeueCount: 5),
                BackgroundExceptionDispatcher.Instance, messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor,
                sharedContextProvider, new DefaultExtensionRegistry(), TextWriter.Null);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(nameResolver, storageAccountProvider,
                extensionTypeLocator, messageEnqueuedWatcherAccessor,
                blobWrittenWatcherAccessor, new DefaultExtensionRegistry());

            IFunctionOutputLoggerProvider outputLoggerProvider = new NullFunctionOutputLoggerProvider();
            var task = outputLoggerProvider.GetAsync(CancellationToken.None);
            task.Wait();
            IFunctionOutputLogger outputLogger = task.Result;

            Task<IStorageAccount> storageAccountTask = storageAccountProvider.GetStorageAccountAsync(CancellationToken.None);
            storageAccountTask.Wait();
            SingletonManager singletonManager = new SingletonManager(storageAccountTask.Result.CreateBlobClient());

            IFunctionExecutor executor = new FunctionExecutor(new NullFunctionInstanceLogger(), outputLogger, BackgroundExceptionDispatcher.Instance, singletonManager);

            if (extensionRegistry == null)
            {
                extensionRegistry = new DefaultExtensionRegistry();
            }

            return new FunctionIndexer(triggerBindingProvider, bindingProvider, DefaultJobActivator.Instance, executor, extensionRegistry);
        }
    }
}
