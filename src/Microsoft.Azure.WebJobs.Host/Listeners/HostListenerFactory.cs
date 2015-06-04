// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class HostListenerFactory : IListenerFactory
    {
        private readonly SingletonManager _singletonManager;
        private readonly IEnumerable<IFunctionDefinition> _functionDefinitions;

        public HostListenerFactory(SingletonManager singletonManager, IEnumerable<IFunctionDefinition> functionDefinitions)
        {
            _singletonManager = singletonManager;
            _functionDefinitions = functionDefinitions;
        }

        public async Task<IListener> CreateAsync(ListenerFactoryContext context)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IFunctionDefinition functionDefinition in _functionDefinitions)
            {
                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;

                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = await listenerFactory.CreateAsync(context);

                SingletonAttribute singletonAttribute = functionDefinition.Method.GetCustomAttribute<SingletonAttribute>();
                if (singletonAttribute != null && singletonAttribute.Mode == SingletonMode.Trigger)
                {
                    listener = new SingletonListener(functionDefinition.Method, singletonAttribute, _singletonManager, listener);
                }

                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
}
