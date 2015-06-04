// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host
{
    internal class SingletonManager
    {
        private readonly TimeSpan _defaultLeasePeriod = TimeSpan.FromSeconds(15);
        private const int LeasePollInterval = 500;

        private readonly IStorageBlobDirectory _directory;

        public SingletonManager(IStorageBlobClient client)
            : this(client.GetContainerReference(HostContainerNames.Hosts)
                .GetDirectoryReference(HostDirectoryNames.SingletonLocks))
        {
        }

        public SingletonManager(IStorageBlobDirectory directory)
        {
            _directory = directory;
        }

        public static string FormatLockId(MethodInfo method, string scope)
        {
            string lockId = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", method.DeclaringType.FullName, method.Name);
            if (!string.IsNullOrEmpty(scope))
            {
                lockId += "." + scope;
            }
            return lockId;
        }

        public async Task<object> LockAsync(string lockId, CancellationToken cancellationToken, TimeSpan? leasePeriod = null, TimeSpan? aquisitionTimeout = null)
        {
            object lockHandle = await TryLockAsync(lockId, cancellationToken, leasePeriod, aquisitionTimeout);

            if (lockHandle == null)
            {
                throw new InvalidOperationException(string.Format("Unable to aquire singleton lock blob lease for blob '{0}'", lockId));
            }

            return lockHandle;
        }

        public async Task<object> TryLockAsync(string lockId, CancellationToken cancellationToken, TimeSpan? leasePeriod = null, TimeSpan? aquisitionTimeout = null)
        {
            IStorageBlockBlob lockBlob = _directory.GetBlockBlobReference(lockId);

            await TryCreateAsync(lockBlob, cancellationToken);

            if (leasePeriod == null)
            {
                leasePeriod = _defaultLeasePeriod;
            }
            string leaseId = await TryAcquireLeaseAsync(lockBlob, leasePeriod.Value, cancellationToken);

            // Someone else has the lease. Continue trying to periodically get the lease for
            // a period of time
            if (string.IsNullOrEmpty(leaseId) && aquisitionTimeout != null)
            {
                double remainingWaitTime = aquisitionTimeout.Value.TotalMilliseconds;
                while (string.IsNullOrEmpty(leaseId) && remainingWaitTime > 0)
                {
                    await Task.Delay(LeasePollInterval);
                    leaseId = await TryAcquireLeaseAsync(lockBlob, leasePeriod.Value, cancellationToken);
                    remainingWaitTime -= LeasePollInterval;
                }
            }

            if (string.IsNullOrEmpty(leaseId))
            {
                return null;
            }

            SingletonLockHandle lockHandle = new SingletonLockHandle
            {
                LeaseId = leaseId,
                Blob = lockBlob,
                LeasePeriod = leasePeriod.Value
            };

            System.Timers.Timer lockRenewalTimer = CreateRenewalTimer(lockHandle);
            lockRenewalTimer.Start();
            lockHandle.RenewalTimer = lockRenewalTimer;

            return lockHandle;
        }

        public async Task ReleaseLockAsync(object lockHandle, CancellationToken cancellationToken)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;

            if (singletonLockHandle.RenewalTimer != null)
            {
                singletonLockHandle.RenewalTimer.Stop();
                singletonLockHandle.RenewalTimer.Dispose();
            }

            await ReleaseLeaseAsync(singletonLockHandle.Blob, singletonLockHandle.LeaseId, cancellationToken);
        }

        private async Task<string> TryAcquireLeaseAsync(IStorageBlockBlob blob, TimeSpan leasePeriod, CancellationToken cancellationToken)
        {
            try
            {
                return await blob.AcquireLeaseAsync(leasePeriod, null, cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsConflictLeaseAlreadyPresent())
                {
                    return null;
                }
                else if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    // If someone deleted the receipt, there's no lease to acquire.
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task ReleaseLeaseAsync(IStorageBlockBlob blob, string leaseId, CancellationToken cancellationToken)
        {
            try
            {
                // Note that this call returns without throwing if the lease is expired. See the table at:
                // http://msdn.microsoft.com/en-us/library/azure/ee691972.aspx
                await blob.ReleaseLeaseAsync(
                    accessCondition: new AccessCondition { LeaseId = leaseId },
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    // The user deleted the receipt or its container; nothing to release at this point.
                }
                else if (exception.IsConflictLeaseIdMismatchWithLeaseOperation())
                {
                    // Another lease is active; nothing for this lease to release at this point.
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task<bool> TryCreateAsync(IStorageBlockBlob blob, CancellationToken cancellationToken)
        {
            AccessCondition accessCondition = new AccessCondition { IfNoneMatchETag = "*" };
            bool isContainerNotFoundException = false;

            try
            {
                await blob.UploadTextAsync(String.Empty,
                    encoding: null,
                    accessCondition: accessCondition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundContainerNotFound())
                {
                    isContainerNotFoundException = true;
                }
                else if (exception.IsConflictBlobAlreadyExists())
                {
                    return false;
                }
                else if (exception.IsPreconditionFailedLeaseIdMissing())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }

            Debug.Assert(isContainerNotFoundException);
            await blob.Container.CreateIfNotExistsAsync(cancellationToken);

            try
            {
                await blob.UploadTextAsync(String.Empty,
                    encoding: null,
                    accessCondition: accessCondition,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
                return true;
            }
            catch (StorageException exception)
            {
                if (exception.IsConflictBlobAlreadyExists())
                {
                    return false;
                }
                else if (exception.IsPreconditionFailedLeaseIdMissing())
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal static System.Timers.Timer CreateRenewalTimer(object lockHandle)
        {
            SingletonLockHandle singletonLockHandle = (SingletonLockHandle)lockHandle;

            System.Timers.Timer renewalTimer = new System.Timers.Timer()
            {
                AutoReset = true,
                Enabled = true,
                Interval = singletonLockHandle.LeasePeriod.TotalMilliseconds / 2
            };
            renewalTimer.Elapsed += async (sender, e) =>
                {
                    AccessCondition condition = new AccessCondition
                    {
                        LeaseId = singletonLockHandle.LeaseId
                    };
                    await singletonLockHandle.Blob.SdkObject.RenewLeaseAsync(condition);
                };

            return renewalTimer;
        }

        private class SingletonLockHandle
        {
            public string LeaseId { get; set; }
            public IStorageBlockBlob Blob { get; set; }
            public TimeSpan LeasePeriod { get; set; }
            public System.Timers.Timer RenewalTimer { get; set; }
        }
    }
}
