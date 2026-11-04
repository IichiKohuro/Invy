using Invy.Desktop.Hosting;

namespace Invy.Client.Tests.Desktop;

public class SingleInstanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Second_acquire_fails_while_first_is_alive()
    {
        var appId = UniqueAppId();

        using var first = SingleInstance.TryAcquire(appId);
        first.ShouldNotBeNull();

        SingleInstance.TryAcquire(appId).ShouldBeNull();
    }

    [Fact]
    public void Can_acquire_again_after_release()
    {
        var appId = UniqueAppId();

        SingleInstance.TryAcquire(appId)!.Dispose();

        using var again = SingleInstance.TryAcquire(appId);
        again.ShouldNotBeNull();
    }

    [Fact]
    public async Task Second_launch_forwards_arguments_to_first_instance()
    {
        var appId = UniqueAppId();
        using var first = SingleInstance.TryAcquire(appId)!;
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ArgumentsReceived += args => received.TrySetResult(args);
        first.StartListening();

        var sent = await SingleInstance.SendAsync(appId, ["--search", "кабель usb-c"], Timeout);

        sent.ShouldBeTrue();
        (await received.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken)).ShouldBe(["--search", "кабель usb-c"]);
    }

    [Fact]
    public async Task Sending_without_running_instance_returns_false() =>
        (await SingleInstance.SendAsync(UniqueAppId(), ["--background"], TimeSpan.FromMilliseconds(300))).ShouldBeFalse();

    private static string UniqueAppId() => $"Invy.Tests.{Guid.NewGuid():N}";
}
