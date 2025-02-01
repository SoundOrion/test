using System;
using System.Threading.Tasks;

namespace TaskScheduler;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("Press S to start, P to stop, Q to quit.");
        while (true)
        {
            switch (Console.ReadKey(true).Key)
            {
                case ConsoleKey.S: await TaskManager.Instance.Start(); break;
                case ConsoleKey.P: await TaskManager.Instance.Stop(); break;
                case ConsoleKey.Q: await TaskManager.Instance.Stop(); Console.WriteLine("Exiting..."); return;
                default: Console.WriteLine("Unknown command. Press S, P, or Q."); break;
            }
        }
    }
}
