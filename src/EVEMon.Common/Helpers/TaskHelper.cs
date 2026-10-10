using System;
using System.Threading;
using System.Threading.Tasks;

namespace EVEMon.Common.Helpers
{
    public static class TaskHelper
    {
        /// <summary>
        /// Runs the IO bound action asynchronously.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This used to wrap <c>Delegate.BeginInvoke</c>, which is not supported on .NET Core;
        /// running the action on the thread pool keeps the same behaviour.
        /// </remarks>
        public static Task RunIOBoundTaskAsync(Action action,
            CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(action, cancellationToken);

        /// <summary>
        /// Runs the IO bound function asynchronously.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This methods purpose is to help developers understand
        /// the concept of running IO bound task using <![CDATA[TaskCompletionSource<T>()]]>
        /// See more at: https://msdn.microsoft.com/en-us/library/hh873177.aspx
        /// </remarks>
        public static Task RunIOBoundTaskAsync(Func<Task> function,
            CancellationToken cancellationToken = default(CancellationToken))
            => RunIOBoundTaskAsync<Task>(function, cancellationToken).Unwrap();

        /// <summary>
        /// Runs the IO bound function asynchronously.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="function">The function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This used to wrap <c>Delegate.BeginInvoke</c>, which is not supported on .NET Core;
        /// running the function on the thread pool keeps the same behaviour.
        /// </remarks>
        public static Task<TResult> RunIOBoundTaskAsync<TResult>(Func<TResult> function,
            CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(function, cancellationToken);

        /// <summary>
        /// Runs the IO bound function asynchronously.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="function">The function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This methods purpose is to help developers understand
        /// the concept of running IO bound task using <![CDATA[TaskCompletionSource<T>()]]>
        /// See more at: https://msdn.microsoft.com/en-us/library/hh873177.aspx
        /// </remarks>
        public static Task<TResult> RunIOBoundTaskAsync<TResult>(Func<Task<TResult>> function,
            CancellationToken cancellationToken = default(CancellationToken))
            => RunIOBoundTaskAsync<Task<TResult>>(function, cancellationToken).Unwrap();

        /// <summary>
        /// Runs the compute bound task asynchronously.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This methods purpose is to help developers understand
        /// the concept of running compute bound task using <![CDATA[Task.Run()]]>
        /// See more at: https://msdn.microsoft.com/en-us/library/hh873177.aspx
        /// </remarks>
        public static Task RunCPUBoundTaskAsync(Action action,
            CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(action, cancellationToken);

        /// <summary>
        /// Runs the compute bound task asynchronously.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This methods purpose is to help developers understand
        /// the concept of running compute bound task using <![CDATA[Task.Run()]]>
        /// See more at: https://msdn.microsoft.com/en-us/library/hh873177.aspx
        /// </remarks>
        public static Task RunCPUBoundTaskAsync(Func<Task> function,
            CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(function, cancellationToken);

        /// <summary>
        /// Runs the compute bound task asynchronously.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="function">The function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This methods purpose is to help developers understand
        /// the concept of running compute bound task using <![CDATA[Task.Run()]]>
        /// See more at: https://msdn.microsoft.com/en-us/library/hh873177.aspx
        /// </remarks>
        public static Task<TResult> RunCPUBoundTaskAsync<TResult>(Func<TResult> function,
            CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(function, cancellationToken);

        /// <summary>
        /// Runs the compute bound task asynchronously.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="function">The function.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        /// <remarks>
        /// This methods purpose is to help developers understand
        /// the concept of running compute bound task using <![CDATA[Task.Run()]]>
        /// See more at: https://msdn.microsoft.com/en-us/library/hh873177.aspx
        /// </remarks>
        public static Task<TResult> RunCPUBoundTaskAsync<TResult>(Func<Task<TResult>> function,
            CancellationToken cancellationToken = default(CancellationToken))
            => Task.Run(function, cancellationToken);
    }
}
