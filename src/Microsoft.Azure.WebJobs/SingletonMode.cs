// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Enumeration of serialization modes supported by <see cref="SingletonAttribute"/>
    /// </summary>
    public enum SingletonMode
    {
        /// <summary>
        /// Using this mode, the singleton lock will be established on each
        /// invocation of a job function. This ensures that only a single
        /// instance of the job function (where instance is defined by job
        /// full name + invocation route parameters) is running on any instance
        /// at any given time.
        /// </summary>
        Function,

        /// <summary>
        /// Using this mode, the singleton lock will be established on 
        /// startup of the job function trigger listener. This ensures that
        /// only a single instance of the job listener is running across all
        /// instances at any given time.
        /// </summary>
        Trigger
    }
}
