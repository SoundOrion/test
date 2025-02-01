namespace TaskScheduler.Models;

public class TaskInfo
{
    public string TaskId { get; set; }
    public string TaskName { get; set; }
    public string TriggerType { get; set; }
    public DateTime? StartTime { get; set; }
    public int Priority { get; set; }
    public string Status { get; set; } = "ACTIVE";
}
