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
	public class SynchronousTester
	{
		public readonly string FileName;
		public static readonly ReadOnlyMemory<byte> Source;


		public SynchronousTester(string fileName = "AsyncFileWriterTest.txt")
		{
			FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
		}

		string Setup()
		{
			var dir = Environment.CurrentDirectory;
			var filePath = Path.Combine(dir, FileName);
			File.Delete(filePath); // Start from scratch. (comment out for further appends.)
			return filePath;
		}

		public Task<(int TotalBytesQueued, TimeSpan AggregateTimeWaiting, TimeSpan Elapsed)> Run(Action<string, Action<Action<ReadOnlyMemory<byte>>>> context)
		{
			if (context == null) throw new ArgumentNullException(nameof(context));
			Contract.EndContractBlock();

			return Task.Run(async () =>
			{
				var filePath = Setup();
				await Task.Delay(1000); // Give it some time.

				var telemetry = new ConcurrentBag<(int bytes, TimeSpan time)>();

				var sw = Stopwatch.StartNew();
				context(filePath, writeHandler =>
				{
					void write(int i)
					{
						var message = SourceBuilder.GetLine(i);
						var t = Stopwatch.StartNew();
						writeHandler(message);
						telemetry.Add((message.Length, t.Elapsed));
					}

					Parallel.For(0, 10000, write);
					Parallel.For(10000, 20000, write);

					//writer.Fault(new Exception("Stop!"));

					Task.Delay(1).Wait();
					Parallel.For(20000, 100000, write);

					Task.Delay(1000).Wait(); // Demonstrate that when nothing buffered the active stream closes.
					Parallel.For(100000, 1000000, write);
				});
				sw.Stop();

				var actualBytes = new FileInfo(filePath).Length;
				var (bytes, time) = telemetry.Aggregate((a, b) => (a.bytes + b.bytes, a.time + b.time));

				Debug.Assert(actualBytes == bytes, $"Actual byte count ({actualBytes}) does not match the queued bytes ({bytes}).");

				await Task.Delay(1);
				return (bytes, time, sw.Elapsed);
			});
		}

		public static async Task RunAndReportToConsole(Action<string, Action<Action<ReadOnlyMemory<byte>>>> context, string fileName = "AsyncFileWriterTest.txt")
			=> (await new SynchronousTester(fileName).Run(context)).EmitToConsole();

		public static Task TestFileStreamSingleThread()
		{
			Console.WriteLine($"File stream standard benchmark.");
			var sw = new Stopwatch();
			return new SynchronousTester().Run((filePath, handler) =>
			{
				// Reuse testing method.
				var queue = new ConcurrentQueue<ReadOnlyMemory<byte>>();
				sw.Start();
				handler(s => queue.Enqueue(s));

				using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
				{
					while (queue.TryDequeue(out var entry))
						fs.Write(entry.Span);
				}
				sw.Stop();
			})
			.ContinueWith(t =>
			{
				Console.WriteLine("Total Elapsed Time: {0} seconds", sw.Elapsed.TotalSeconds);
				Console.WriteLine("------------------------");
				Console.WriteLine();
			});
		}

		public static Task TestAsyncFileStreamSingleThread()
		{
			Console.WriteLine($"File stream async benchmark.");
			var sw = new Stopwatch();
			return new SynchronousTester().Run((filePath, handler) =>
			{
				Task.Run(async () =>
				{
					var queue = new ConcurrentQueue<ReadOnlyMemory<byte>>();
					sw.Start();
					// Reuse testing method.
					handler(s => queue.Enqueue(s));

					using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true))
					{
						while (queue.TryDequeue(out var entry))
							await fs.WriteAsync(entry);

						await fs.FlushAsync();
					}
					sw.Stop();
				}).Wait();
			})
			.ContinueWith(t =>
			{
				Console.WriteLine("Total Elapsed Time: {0} seconds", sw.Elapsed.TotalSeconds);
				Console.WriteLine("------------------------");
				Console.WriteLine();
			});
		}

		public static Task TestSynchronizedFileStream()
		{
			Console.WriteLine($"Synchronized file stream benchmark.");
			return RunAndReportToConsole((filePath, handler) =>
			{
				using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
				{
					handler(s =>
					{
						lock (fs)
							fs.Write(s.Span);
					});
				}
			});
		}

		public static Task TestBlockingCollection(int boundedCapacity)
		{
			Console.WriteLine("{0:#,##0} BlockingCollection queued benchmark.", boundedCapacity);
			return RunAndReportToConsole((filePath, handler) =>
			{
				var queue = new BlockingCollection<ReadOnlyMemory<byte>>(boundedCapacity);
				Task.Run(() =>
				{
					handler(s => queue.Add(s));
					queue.CompleteAdding();
				});
				using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
				{
					foreach (var s in queue.GetConsumingEnumerable())
						fs.Write(s.Span);
				}
			});
		}

		public static Task TestMultipleFileStreams()
		{
			Console.WriteLine($"Multiple file stream benchmark.");
			return RunAndReportToConsole((filePath, handler) =>
			{
				handler(s =>
				{
					using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Write))
						fs.Write(s.Span);
				});
			});
		}

		public static Task TestAsyncFileWriter(int boundedCapacity)
		{
			Console.WriteLine("{0:#,##0} bounded capacity.", boundedCapacity);
			return RunAndReportToConsole((filePath, handler) =>
			{
				using (var writer = new AsyncFileWriter(filePath, boundedCapacity))
					handler(s => writer.Add(s));
			});
		}

	}
}
