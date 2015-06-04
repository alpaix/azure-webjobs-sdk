// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// This attribute when applied to a job function trigger parameter
    /// serializes execution of the job function according to the
    /// <see cref="SingletonMode"/> specified.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SingletonAttribute : Attribute
    {
        /// <summary>
        /// Constructs a new instance
        /// </summary>
        /// <param name="mode">The lock mode to use.</param>
        public SingletonAttribute(SingletonMode mode)
            : this(string.Empty, mode)
        {
        }

        /// <summary>
        /// Constructs a new instance
        /// </summary>
        /// <param name="scope">The scope identifier for the singleton lock. This value can include route parameters.</param>
        /// <param name="mode">The lock mode to use.</param>
        public SingletonAttribute(string scope, SingletonMode mode)
        {
            Scope = scope;
            Mode = mode;
        }

        /// <summary>
        /// Gets the scope identifier for the singleton lock.
        /// </summary>
        /// <remarks>
        /// The lock ID is the global key that will be used across instances to
        /// establish a singleton lock.
        /// </remarks>
        public string Scope
        {
            get; 
            private set;
        }

        /// <summary>
        /// Gets the <see cref="SingletonMode"/> that will be used.
        /// </summary>
        public SingletonMode Mode
        {
            get;
            private set;
        }
    }
}
