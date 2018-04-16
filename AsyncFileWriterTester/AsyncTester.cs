using Open;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncFileWriterTester
{
	public class AsyncTester
	{
		public readonly string FileName;

		public AsyncTester(string fileName = "AsyncFileWriterTest.txt")
		{
			FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
		}

		public async Task<(int TotalBytesQueued, TimeSpan AggregateTimeWaiting, TimeSpan Elapsed)> Run(Func<string, Func<Func<string, Task>, Task>, Task> context)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			Contract.EndContractBlock();

			var dir = Environment.CurrentDirectory;
			var filePath = Path.Combine(dir, FileName);
			File.Delete(filePath); // Start from scratch. (comment out for further appends.)

			var telemetry = new ConcurrentBag<(int bytes, TimeSpan time)>();

			var sw = Stopwatch.StartNew();
			await context(filePath, async writeHandler =>
			{
				void write(int i)
				{
					var message = $"{i}) {DateTime.Now} 00000000000000000000000000000000111111111111111111111111111222222222222222222222222222\n";
					var t = Stopwatch.StartNew();
					writeHandler(message).Wait();
					telemetry.Add((message.Length, t.Elapsed));
				}

				Parallel.For(0, 10000, write);
				Parallel.For(10000, 20000, write);

					//writer.Fault(new Exception("Stop!"));

				Task.Delay(1).Wait();
				Parallel.For(20000, 100000, write);

				Task.Delay(1000).Wait(); // Demonstrate that when nothing buffered the active stream closes.
					Parallel.For(100000, 1000000, write);

				await Task.Yield();
			});
			sw.Stop();

			var (bytes, time) = telemetry.Aggregate((a, b) => (a.bytes + b.bytes, a.time + b.time));
			return (bytes, time, sw.Elapsed);
		}

		public static async Task RunAndReportToConsole(Func<string, Func<Func<string, Task>, Task>, Task> context, string fileName = "AsyncFileWriterTest.txt")
			=> (await new AsyncTester(fileName).Run(context)).EmitToConsole();

		public static Task TestAsyncFileWriter(int boundedCapacity = -1)
		{
			Console.WriteLine("{0:#,##0} bounded capacity.", boundedCapacity);
			return RunAndReportToConsole(async (filePath, handler) =>
			{
				using (var writer = new AsyncFileWriter(filePath, boundedCapacity, asyncFileStream: true))
					await handler(s => writer.AddAsync(s));
			});
		}
	}
}
