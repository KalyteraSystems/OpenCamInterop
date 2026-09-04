using OpenCamInterop.EventLab;

using var cancellation = new CancellationTokenSource();

ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.CancelKeyPress += cancelHandler;

try
{
    return await EventLabApplication.CreateDefault().RunAsync(args, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
