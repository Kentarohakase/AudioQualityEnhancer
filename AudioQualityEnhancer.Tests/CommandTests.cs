using AudioQualityEnhancer.ViewModels;

namespace AudioQualityEnhancer.Tests;

public sealed class CommandTests
{
    [Fact]
    public void AsyncRelayCommand_WithoutPredicate_CanExecute()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommand_HonoursItsPredicate()
    {
        var allowed = false;
        var command = new AsyncRelayCommand(() => Task.CompletedTask, () => allowed);

        Assert.False(command.CanExecute(null));

        allowed = true;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommand_BlocksReentryWhileRunning()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            executions++;
            await gate.Task;
        });

        command.Execute(null);
        Assert.False(command.CanExecute(null));

        // A second click while the first run is still in flight must be ignored.
        command.Execute(null);
        Assert.Equal(1, executions);

        gate.SetResult();
        await WaitUntilAsync(() => command.CanExecute(null));
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task AsyncRelayCommand_ReportsFailureToTheErrorHandler()
    {
        Exception? reported = null;
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("boom"),
            onError: exception => reported = exception);

        command.Execute(null);
        await WaitUntilAsync(() => reported is not null);

        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal("boom", reported!.Message);
    }

    [Fact]
    public async Task AsyncRelayCommand_TreatsCancellationAsANormalOutcome()
    {
        Exception? reported = null;
        var command = new AsyncRelayCommand(
            () => throw new OperationCanceledException(),
            onError: exception => reported = exception);

        command.Execute(null);
        await WaitUntilAsync(() => command.CanExecute(null));

        Assert.Null(reported);
    }

    [Fact]
    public async Task AsyncRelayCommand_BecomesExecutableAgainAfterAFailure()
    {
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException(),
            onError: _ => { });

        command.Execute(null);
        await WaitUntilAsync(() => command.CanExecute(null));

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void AsyncRelayCommand_DoesNotRunWhenItCannotExecute()
    {
        var executions = 0;
        var command = new AsyncRelayCommand(
            () =>
            {
                executions++;
                return Task.CompletedTask;
            },
            () => false);

        command.Execute(null);

        Assert.Equal(0, executions);
    }

    [Fact]
    public void RelayCommand_RaisesCanExecuteChanged()
    {
        var raised = 0;
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => raised++;

        command.RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void RelayCommand_ExecutesAndHonoursItsPredicate()
    {
        var executions = 0;
        var allowed = true;
        var command = new RelayCommand(() => executions++, () => allowed);

        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(1, executions);

        allowed = false;
        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void RelayCommandOfT_PassesTheTypedParameter()
    {
        string? received = null;
        var command = new RelayCommand<string>(value => received = value);

        command.Execute("preset");

        Assert.Equal("preset", received);
    }

    [Fact]
    public void RelayCommandOfT_PassesDefaultForAMismatchedParameter()
    {
        var executions = 0;
        string? received = "unchanged";
        var command = new RelayCommand<string>(value =>
        {
            executions++;
            received = value;
        });

        command.Execute(42);

        Assert.Equal(1, executions);
        Assert.Null(received);
    }

    [Fact]
    public void RelayCommandOfT_EvaluatesThePredicateForNullAndForAValue()
    {
        var command = new RelayCommand<string>(_ => { }, value => value is not null);

        Assert.False(command.CanExecute(null));
        Assert.True(command.CanExecute("preset"));

        // A parameter of a different type is neither T nor null, so it cannot execute.
        Assert.False(command.CanExecute(42));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }
}
