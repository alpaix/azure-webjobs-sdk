using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Configuration for trigger host
    /// </summary>
    public sealed class TriggerHostConfiguration : IServiceProvider
    {
        private readonly IJobHostContextFactory _contextFactory = new TriggerHostContextFactory();

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IJobHostContextFactory))
            {
                return _contextFactory;
            }

            return null;
        }
    }

    internal class TriggerHostContextFactory : IJobHostContextFactory
    {
        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
