using System;
using System.Threading;
using System.Threading.Tasks;
using TaskScheduler.Models;

namespace TaskScheduler.Services;

public class TaskProcessor
{
    private readonly NatsService _natsService;
    private readonly SemaphoreSlim _semaphore = new(3, 3);

    public TaskProcessor(NatsService natsService)
    {
        _natsService = natsService;
    }

    public async Task ProcessTask(TaskInfo taskInfo, CancellationToken token)
    {
        await _semaphore.WaitAsync(token);
        try
        {
            _natsService.PublishTask(taskInfo);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
