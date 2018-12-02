using Open.Threading;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
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

		public async Task<(int TotalBytesQueued, TimeSpan AggregateTimeWaiting, TimeSpan Elapsed)> Run(Func<string, Func<Func<ReadOnlyMemory<byte>, Task>, Task>, Task> context)
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
				async Task write(int i)
				{
					var message = SourceBuilder.Source[i];
					var t = Stopwatch.StartNew();
					await writeHandler(message);
					telemetry.Add((message.Length, t.Elapsed));
				}

				await ParallelAsync.ForAsync(0, 10000, write);
				await ParallelAsync.ForAsync(10000, 20000, write);

				//writer.Fault(new Exception("Stop!"));

				await Task.Delay(1);
				await ParallelAsync.ForAsync(20000, 100000, write);

				await Task.Delay(1000); // Demonstrate that when nothing buffered the active stream closes.
				await ParallelAsync.ForAsync(100000, 1000000, write);
			});
			sw.Stop();

			var actualBytes = new FileInfo(filePath).Length;
			var (bytes, time) = telemetry.Aggregate((a, b) => (a.bytes + b.bytes, a.time + b.time));

			Debug.Assert(actualBytes == bytes, "Actual byte count does not match the queued bytes.");

			return (bytes, time, sw.Elapsed);
		}

		public static async Task RunAndReportToConsole(Func<string, Func<Func<ReadOnlyMemory<byte>, Task>, Task>, Task> context, string fileName = "AsyncFileWriterTest.txt")
			=> (await new AsyncTester(fileName).Run(context)).EmitToConsole();

		public static Task TestAsyncFileWriter(int boundedCapacity, bool asyncFileWrite)
		{
			Console.WriteLine("{0:#,##0} bounded capacity.", boundedCapacity);
			return RunAndReportToConsole(async (filePath, handler) =>
			{
				var writer = new AsyncFileWriter(filePath, boundedCapacity, asyncFileStream: true, asyncFileWrite: asyncFileWrite);
				await handler(s => writer.AddAsync(s));
				await writer.DisposeAsync();
			});
		}


	}
}
