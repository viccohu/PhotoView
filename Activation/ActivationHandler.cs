using PhotoView.Contracts.Services;

namespace PhotoView.Activation;

public abstract class ActivationHandler<T> : IActivationHandler
    where T : class
{
    public bool CanHandle(object args)
    {
        return args is T typedArgs && CanHandleInternal(typedArgs);
    }

    public async Task HandleAsync(object args)
    {
        if (args is not T typedArgs)
        {
            throw new ArgumentException($"Activation arguments must be of type {typeof(T).Name}.", nameof(args));
        }

        await HandleInternalAsync(typedArgs);
    }

    protected virtual bool CanHandleInternal(T args)
    {
        return true;
    }

    protected abstract Task HandleInternalAsync(T args);
}

public interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
