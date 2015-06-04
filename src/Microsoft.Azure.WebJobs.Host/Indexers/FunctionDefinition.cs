// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class FunctionDefinition : IFunctionDefinition
    {
        private readonly MethodInfo _method;
        private readonly IFunctionInstanceFactory _instanceFactory;
        private readonly IListenerFactory _listenerFactory;

        public FunctionDefinition(MethodInfo method, IFunctionInstanceFactory instanceFactory, IListenerFactory listenerFactory)
        {
            _method = method;
            _instanceFactory = instanceFactory;
            _listenerFactory = listenerFactory;
        }

        public MethodInfo Method
        {
            get
            {
                return _method;
            }
        }

        public IFunctionInstanceFactory InstanceFactory
        {
            get { return _instanceFactory; }
        }

        public IListenerFactory ListenerFactory
        {
            get { return _listenerFactory; }
        }
    }
}
