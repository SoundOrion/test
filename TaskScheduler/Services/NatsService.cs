using System;
using System.Text;
using System.Text.Json;
using NATS.Client;
using TaskScheduler.Models;

namespace TaskScheduler.Services;

public class NatsService : IDisposable
{
    private readonly string _natsUrl;
    private IConnection _connection;

    public NatsService(string natsUrl)
    {
        _natsUrl = natsUrl ?? "nats://localhost:4222";
    }

    public void Connect()
    {
        var cf = new ConnectionFactory();
        _connection = cf.CreateConnection(_natsUrl);
    }

    public void PublishTask(TaskInfo taskInfo)
    {
        string json = JsonSerializer.Serialize(taskInfo);
        _connection.Publish("task.process", Encoding.UTF8.GetBytes(json));
        _connection.Flush();
    }

    public void SubscribeToTaskCompletion(Action<TaskCompletionResult> onTaskCompleted)
    {
        var subscription = _connection.SubscribeAsync("task.complete");

        subscription.MessageHandler += (sender, msg) =>
        {
            try
            {
                var result = JsonSerializer.Deserialize<TaskCompletionResult>(Encoding.UTF8.GetString(msg.Data));
                onTaskCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NATS subscriber error: {ex.Message}");
            }
        };

        subscription.Start();
    }

    public void Dispose()
    {
        _connection?.Drain();
        _connection?.Dispose();
    }
}
