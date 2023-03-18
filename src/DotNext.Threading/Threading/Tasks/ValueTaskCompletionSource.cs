using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using Debug = System.Diagnostics.Debug;

namespace DotNext.Threading.Tasks;

using NullExceptionConstant = Generic.DefaultConst<Exception?>;

/// <summary>
/// Represents the producer side of <see cref="ValueTask"/>.
/// </summary>
/// <remarks>
/// See description of <see cref="ValueTaskCompletionSource{T}"/> for more information
/// about behavior of the completion source.
/// </remarks>
/// <seealso cref="ValueTaskCompletionSource{T}"/>
public class ValueTaskCompletionSource : ManualResetCompletionSource, IValueTaskSource, ISupplier<TimeSpan, CancellationToken, ValueTask>
{
    [StructLayout(LayoutKind.Auto)]
    private readonly struct OperationCanceledExceptionFactory : ISupplier<OperationCanceledException>
    {
        private readonly CancellationToken token;

        internal OperationCanceledExceptionFactory(CancellationToken token) => this.token = token;

        OperationCanceledException ISupplier<OperationCanceledException>.Invoke()
            => new(token);

        public static implicit operator OperationCanceledExceptionFactory(CancellationToken token)
            => new(token);
    }

    private sealed class LinkedTaskCompletionSource : TaskCompletionSource
    {
        private static readonly Action<object?> CompletionCallback = OnCompleted;

        private static void OnCompleted(object? state)
        {
            Debug.Assert(state is LinkedTaskCompletionSource);

            Unsafe.As<LinkedTaskCompletionSource>(state).OnCompleted();
        }

        private IValueTaskSource? source;
        private short version;

        internal LinkedTaskCompletionSource(object? state)
            : base(state, TaskCreationOptions.None)
        {
        }

        internal void LinkTo(IValueTaskSource source, short version)
        {
            this.source = source;
            this.version = version;
            source.OnCompleted(CompletionCallback, this, version, ValueTaskSourceOnCompletedFlags.None);
        }

        private void OnCompleted()
        {
            if (source is not null)
            {
                try
                {
                    source.GetResult(version);
                    TrySetResult();
                }
                catch (OperationCanceledException e)
                {
                    TrySetCanceled(e.CancellationToken);
                }
                catch (Exception e)
                {
                    TrySetException(e);
                }
            }

            source = null;
        }
    }

    private static readonly NullExceptionConstant NullSupplier = new();

    // null - success, not null - error
    private ExceptionDispatchInfo? result;

    /// <summary>
    /// Initializes a new completion source.
    /// </summary>
    /// <param name="runContinuationsAsynchronously">Indicates that continuations must be executed asynchronously.</param>
    public ValueTaskCompletionSource(bool runContinuationsAsynchronously = true)
        : base(runContinuationsAsynchronously)
    {
    }

    private CompletionResult SetResult(Exception? result, object? completionData = null)
    {
        Debug.Assert(Monitor.IsEntered(SyncRoot));

        this.result = result is null ? null : ExceptionDispatchInfo.Capture(result);
        versionAndStatus.Status = ManualResetCompletionSourceStatus.WaitForConsumption;
        return Complete(completionData);
    }

    private protected sealed override CompletionResult CompleteAsTimedOut()
        => SetResult(OnTimeout());

    private protected sealed override CompletionResult CompleteAsCanceled(CancellationToken token)
        => SetResult(OnCanceled(token));

    /// <inheritdoc />
    protected override void Cleanup() => result = null;

    /// <summary>
    /// Called automatically when timeout detected.
    /// </summary>
    /// <remarks>
    /// By default, this method returns <see cref="TimeoutException"/> as the task result.
    /// </remarks>
    /// <returns>The exception representing task result; or <see langword="null"/> to complete successfully.</returns>
    protected virtual Exception? OnTimeout() => new TimeoutException();

    /// <summary>
    /// Called automatically when cancellation detected.
    /// </summary>
    /// <remarks>
    /// By default, this method returns <see cref="OperationCanceledException"/> as the task result.
    /// </remarks>
    /// <param name="token">The token representing cancellation reason.</param>
    /// <returns>The exception representing task result; or <see langword="null"/> to complete successfully.</returns>
    protected virtual Exception? OnCanceled(CancellationToken token) => new OperationCanceledException(token);

    private CompletionResult SetResult<TFactory>(TFactory factory, object? completionData, short? completionToken)
        where TFactory : notnull, ISupplier<Exception?>
    {
        CompletionResult result;

        if (versionAndStatus.CanBeCompleted)
        {
            lock (SyncRoot)
            {
                result = versionAndStatus.CanBeCompleted && (completionToken is null || completionToken.GetValueOrDefault() == versionAndStatus.Version)
                    ? SetResult(factory.Invoke(), completionData)
                    : default;
            }
        }
        else
        {
            result = default;
        }

        return result;
    }

    /// <inheritdoc />
    public sealed override bool TrySetCanceled(object? completionData, CancellationToken token)
        => SetResult<OperationCanceledExceptionFactory>(token, completionData, completionToken: null).NotifyListener(runContinuationsAsynchronously);

    /// <summary>
    /// Attempts to complete the task unsuccessfully.
    /// </summary>
    /// <param name="completionToken">The completion token previously obtained from <see cref="CreateTask(TimeSpan, CancellationToken)"/> method.</param>
    /// <param name="token">The canceled token.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetCanceled(short completionToken, CancellationToken token)
        => TrySetCanceled(null, completionToken, token);

    /// <summary>
    /// Attempts to complete the task unsuccessfully.
    /// </summary>
    /// <param name="completionData">The data to be saved in <see cref="ManualResetCompletionSource.CompletionData"/> property that can be accessed from within <see cref="ManualResetCompletionSource.AfterConsumed"/> method.</param>
    /// <param name="completionToken">The completion token previously obtained from <see cref="CreateTask(TimeSpan, CancellationToken)"/> method.</param>
    /// <param name="token">The canceled token.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    public bool TrySetCanceled(object? completionData, short completionToken, CancellationToken token)
        => SetResult<OperationCanceledExceptionFactory>(token, completionData, completionToken).NotifyListener(runContinuationsAsynchronously);

    /// <inheritdoc />
    public sealed override bool TrySetException(object? completionData, Exception e)
        => SetResult<ValueSupplier<Exception>>(e, completionData, completionToken: null).NotifyListener(runContinuationsAsynchronously);

    /// <summary>
    /// Attempts to complete the task unsuccessfully.
    /// </summary>
    /// <param name="completionToken">The completion token previously obtained from <see cref="CreateTask(TimeSpan, CancellationToken)"/> method.</param>
    /// <param name="e">The exception to be returned to the consumer.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetException(short completionToken, Exception e)
        => TrySetException(null, completionToken, e);

    /// <summary>
    /// Attempts to complete the task unsuccessfully.
    /// </summary>
    /// <param name="completionData">The data to be saved in <see cref="ManualResetCompletionSource.CompletionData"/> property that can be accessed from within <see cref="ManualResetCompletionSource.AfterConsumed"/> method.</param>
    /// <param name="completionToken">The completion token previously obtained from <see cref="CreateTask(TimeSpan, CancellationToken)"/> method.</param>
    /// <param name="e">The exception to be returned to the consumer.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    public bool TrySetException(object? completionData, short completionToken, Exception e)
        => SetResult<ValueSupplier<Exception>>(e, completionData, completionToken).NotifyListener(runContinuationsAsynchronously);

    /// <summary>
    /// Attempts to complete the task sucessfully.
    /// </summary>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetResult()
        => TrySetResult(null);

    /// <summary>
    /// Attempts to complete the task sucessfully.
    /// </summary>
    /// <param name="completionData">The data to be saved in <see cref="ManualResetCompletionSource.CompletionData"/> property that can be accessed from within <see cref="ManualResetCompletionSource.AfterConsumed"/> method.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    public bool TrySetResult(object? completionData)
        => SetResult(NullSupplier, completionData, completionToken: null).NotifyListener(runContinuationsAsynchronously);

    /// <summary>
    /// Attempts to complete the task sucessfully.
    /// </summary>
    /// <param name="completionToken">The completion token previously obtained from <see cref="CreateTask(TimeSpan, CancellationToken)"/> method.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    public bool TrySetResult(short completionToken)
        => TrySetResult(null, completionToken);

    /// <summary>
    /// Attempts to complete the task sucessfully.
    /// </summary>
    /// <param name="completionData">The data to be saved in <see cref="ManualResetCompletionSource.CompletionData"/> property that can be accessed from within <see cref="ManualResetCompletionSource.AfterConsumed"/> method.</param>
    /// <param name="completionToken">The completion token previously obtained from <see cref="CreateTask(TimeSpan, CancellationToken)"/> method.</param>
    /// <returns><see langword="true"/> if the result is completed successfully; <see langword="false"/> if the task has been canceled or timed out.</returns>
    public bool TrySetResult(object? completionData, short completionToken)
        => SetResult<NullExceptionConstant>(NullSupplier, completionData, completionToken).NotifyListener(runContinuationsAsynchronously);

    /// <summary>
    /// Creates a fresh task linked with this source.
    /// </summary>
    /// <remarks>
    /// This method must be called after <see cref="ManualResetCompletionSource.Reset()"/>.
    /// </remarks>
    /// <param name="timeout">The timeout associated with the task.</param>
    /// <param name="token">The cancellation token that can be used to cancel the task.</param>
    /// <returns>A fresh incompleted task.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is less than zero but not equals to <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="InvalidOperationException">The source is in invalid state.</exception>
    public ValueTask CreateTask(TimeSpan timeout, CancellationToken token)
    {
        if (!PrepareTask(timeout, token))
            InvalidSourceStateDetected();

        return new(this, versionAndStatus.Version);
    }

    /// <inheritdoc />
    ValueTask ISupplier<TimeSpan, CancellationToken, ValueTask>.Invoke(TimeSpan timeout, CancellationToken token)
        => CreateTask(timeout, token);

    /// <inheritdoc />
    void IValueTaskSource.GetResult(short token)
    {
        // ensure that instance field access before returning to the pool to avoid
        // concurrency with Reset()
        var resultCopy = result;
        versionAndStatus.Consume(token);
        AfterConsumed();
        resultCopy?.Throw();
    }

    /// <inheritdoc />
    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        var snapshot = versionAndStatus;
        if (token != snapshot.Version)
            throw new InvalidOperationException(ExceptionMessages.InvalidSourceToken);

        return !snapshot.IsCompleted ? ValueTaskSourceStatus.Pending : result switch
        {
            null => ValueTaskSourceStatus.Succeeded,
            { SourceException: OperationCanceledException } => ValueTaskSourceStatus.Canceled,
            _ => ValueTaskSourceStatus.Faulted,
        };
    }

    /// <inheritdoc />
    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => OnCompleted(continuation, state, token, flags);

    /// <summary>
    /// Creates a linked <see cref="TaskCompletionSource"/> that can be used cooperatively to
    /// complete the task.
    /// </summary>
    /// <param name="userData">The custom data to be associated with the current version of the task.</param>
    /// <param name="timeout">The timeout associated with the task.</param>
    /// <param name="token">The cancellation token that can be used to cancel the task.</param>
    /// <returns>A linked <see cref="TaskCompletionSource"/>.</returns>
    /// <exception cref="InvalidOperationException">The source is in invalid state.</exception>
    public TaskCompletionSource CreateLinkedTaskCompletionSource(object? userData, TimeSpan timeout, CancellationToken token)
    {
        if (!PrepareTask(timeout, token))
            InvalidSourceStateDetected();

        var source = new LinkedTaskCompletionSource(userData);
        source.LinkTo(this, versionAndStatus.Version);
        return source;
    }
}