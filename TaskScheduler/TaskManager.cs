using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TaskScheduler.Models;
using TaskScheduler.Services;

namespace TaskScheduler;

public sealed class TaskManager
{
    private static readonly Lazy<TaskManager> _instance = new(() => new TaskManager());
    public static TaskManager Instance => _instance.Value;

    private readonly DatabaseService _dbService;
    private readonly NatsService _natsService;
    private readonly TaskProcessor _taskProcessor;
    private readonly BlockingCollection<TaskInfo> _taskQueue = new(new ConcurrentQueue<TaskInfo>());
    private CancellationTokenSource _cts;
    private bool _isRunning;
    private PeriodicTimer _timer;

    private TaskManager()
    {
        _dbService = new DatabaseService(Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? throw new InvalidOperationException("Database connection string is not set."));
        _natsService = new NatsService(Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222");
        _taskProcessor = new TaskProcessor(_natsService);
    }

    public async Task Start()
    {
        if (_isRunning)
        {
            Console.WriteLine("TaskManager is already running.");
            return;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        _natsService.Connect();
        _natsService.SubscribeToTaskCompletion(OnTaskCompleted);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        _ = Task.Run(() => TimerLoop(_cts.Token));
        _ = Task.Run(() => ProcessQueue(_cts.Token));
    }

    public async Task Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts.Cancel();
        _taskQueue.CompleteAdding();
        _natsService.Dispose();
    }

    private async Task TimerLoop(CancellationToken token)
    {
        while (await _timer.WaitForNextTickAsync(token))
        {
            if (token.IsCancellationRequested) break;

            var tasks = await _dbService.GetActiveTasksAsync();
            foreach (var task in tasks)
            {
                _taskQueue.Add(task);
            }
        }
    }

    private async Task ProcessQueue(CancellationToken token)
    {
        foreach (var taskInfo in _taskQueue.GetConsumingEnumerable(token))
        {
            await _taskProcessor.ProcessTask(taskInfo, token);
        }
    }

    private void OnTaskCompleted(TaskCompletionResult result)
    {
        Console.WriteLine($"Task {result.TaskId} completed with status {result.Status}.");
    }
}
