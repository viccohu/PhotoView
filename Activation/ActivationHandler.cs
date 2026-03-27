using PhotoView.Contracts.Services;

namespace PhotoView.Activation;

public abstract class ActivationHandler<T> : IActivationHandler
    where T : class
{
    public bool CanHandle(object args)
    {
        return args is T && CanHandleInternal(args as T);
    }

    public async Task HandleAsync(object args)
    {
        await HandleInternalAsync(args as T);
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
