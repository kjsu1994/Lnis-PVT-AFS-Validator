using LnisAfsValidator.App;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Tests;

public sealed class AfsReceiverViewModelTests
{
    [Fact]
    public async Task CompletedSession_AutomaticallyWaitsAgain_AndCancelPreservesLastVerdict()
    {
        var service = new RepeatingReceiverService();
        var viewModel = new AfsReceiverViewModel(service);

        viewModel.StartCommand.Execute(null);
        await service.SecondReceiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, service.ReceiveCount);
        Assert.Equal(Verdict.Pass.ToString(), viewModel.Verdict);

        viewModel.CancelCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.StartCommand.CanExecute(null));

        Assert.Equal("Cancelled", viewModel.State);
        Assert.Equal(Verdict.Pass.ToString(), viewModel.Verdict);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class RepeatingReceiverService : IAfsSessionService
    {
        public int ReceiveCount { get; private set; }
        public TaskCompletionSource SecondReceiveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AfsSessionResult> SendAsync(AfsSenderSettings settings, AfsTransportSettings transport, IProgress<AfsSessionProgress>? progress, CancellationToken token) =>
            throw new NotSupportedException();

        public async Task<AfsSessionResult> ReceiveAsync(AfsReceiverSettings settings, AfsTransportSettings transport, IProgress<AfsSessionProgress>? progress, CancellationToken token)
        {
            ReceiveCount++;
            if (ReceiveCount == 1) return Result();

            SecondReceiveStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancellation was expected.");
        }

        private static AfsSessionResult Result() => new(
            Guid.NewGuid(),
            Verdict.Pass,
            DateTimeOffset.UtcNow,
            new(true, 1, 1, "source", "source", 1, 1, "Complete"),
            [],
            new(1, 1, 1, 1, 0, 0, 0, 0, 1, TimeSpan.Zero, []),
            Path.GetTempPath());
    }
}
