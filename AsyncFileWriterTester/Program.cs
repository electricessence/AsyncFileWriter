using Open;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AsyncFileWriterTester
{
	class Program
	{
		static void Main(string[] args)
		{
			var dir = Environment.CurrentDirectory;
			var fileName = args.FirstOrDefault() ?? "AsyncFileWriterTest.txt";
			var filePath = Path.Combine(dir, fileName);
			File.Delete(filePath); // Start from scratch. (comment out for further appends.)

			Console.WriteLine($"Writing to file: {filePath}");
			using (var writer = new AsyncFileWriter(filePath, 10000000))
			{
				int count = 0;
				void write(int i)
				{
					var message = $"{i}) {DateTime.Now}\n";

					while (!writer.Post(message)
						&& !writer.Completion.IsCompleted
						&& !writer.SendAsync(message).Result
						&& !writer.Completion.IsCompleted)
					{
						Task.Delay(1); // Wait to retry...
					}

					Interlocked.Increment(ref count);
				}

				Parallel.For(0, 10000, write);
				Parallel.For(10000, 20000, write);

				//writer.Fault(new Exception("Stop!"));

				Task.Delay(1).Wait();
				Parallel.For(20000, 100000, write);

				Task.Delay(1000).Wait(); // Demonstrate that when nothing buffered the active stream closes.
				Parallel.For(100000, 1000000, write);

				Debug.WriteLine($"Total Posted: {count}");
				if (writer.Completion.IsFaulted)
					throw writer.Completion.Exception;
			}
		}
	}
}
