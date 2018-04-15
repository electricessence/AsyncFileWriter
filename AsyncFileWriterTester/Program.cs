using Open;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AsyncFileWriterTester
{
	class Program
	{
		static async Task Main(string[] args)
		{
			//await TestSynchronizedFileStream();
			await TestAsyncFileWriter(100000);
			await TestAsyncFileWriter(10000);
			await TestAsyncFileWriter(1000);
			await TestAsyncFileWriter(100);
			//await TestMultipleFileStreams();

			Console.WriteLine("Press ENTER to continue.");
			Console.ReadLine();
		}

		static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1);

		static Task TestSynchronizedFileStream()
		{
			Console.WriteLine($"Synchronized file stream benchmark.");
			return TestAsync(async (filePath, handler) =>
			{
				using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: 4096 * 4, useAsync: true))
				using (var sw = new StreamWriter(fs))
				{
					await handler(async s =>
					{
						await semaphoreSlim.WaitAsync().ConfigureAwait(false);
						try
						{
							await sw.WriteAsync(s).ConfigureAwait(false);
						}
						finally
						{
							semaphoreSlim.Release();
						}
					});

					// FlushAsync here rather than block in Dispose on Flush
					await sw.FlushAsync().ConfigureAwait(false);
					await fs.FlushAsync().ConfigureAwait(false);
				}
			});
		}

		static Task TestMultipleFileStreams()
		{
			Console.WriteLine($"Multiple file stream benchmark.");
			return TestAsync(async (filePath, handler) =>
			{
				await handler(async s =>
				{
					using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Write, bufferSize: 4096 * 4, useAsync: true))
					using (var sw = new StreamWriter(fs))
					{
						await sw.WriteAsync(s).ConfigureAwait(false);

						// FlushAsync here rather than block in Dispose on Flush
						await sw.FlushAsync().ConfigureAwait(false);
						await fs.FlushAsync().ConfigureAwait(false);
					}
				});
			});
		}

		static Task TestAsyncFileWriter(int boundedCapacity = -1)
		{
			Console.WriteLine("{0:#,##0} bounded capacity.", boundedCapacity);
			return TestAsync(async (filePath, asyncHandler) =>
			{
				var writer = new AsyncFileWriter(filePath, boundedCapacity);
				try
				{
					await asyncHandler(s => writer.SendAsync(s)).ConfigureAwait(false);
				}
				finally
				{
					await writer.DisposeAsync().ConfigureAwait(false);
				}
			});
		}

		static ConcurrentBag<Telemetry> Counter = new ConcurrentBag<Telemetry>();
		static int count = 0;

		struct Telemetry
		{
			public int Bytes;
			public TimeSpan Time;
		}

		static async Task TestAsync(Func<string, Func<Func<string, Task>, Task>, Task> context)
		{
			Counter.Clear();
			count = 0;

			var dir = Environment.CurrentDirectory;
			var filePath = Path.Combine(dir, "AsyncFileWriterTest.txt");
			File.Delete(filePath); // Start from scratch. (comment out for further appends.)

			var sw = Stopwatch.StartNew();
			await context(filePath, asyncHandler => RunAsync(asyncHandler)).ConfigureAwait(false);

			Console.WriteLine($"Total Time: {sw.Elapsed.TotalSeconds} seconds");
			Console.WriteLine($"Total Bytes: {Counter.Aggregate((b, c) => new Telemetry() { Bytes = b.Bytes + c.Bytes }).Bytes:#,##0}");
			Console.WriteLine($"Total Wait Time: {Counter.Aggregate((b, c) => new Telemetry() { Time = b.Time + c.Time }).Time}");
			Console.WriteLine("------------------------");
			Console.WriteLine();
		}

		static async Task RunAsync(Func<string, Task> asyncHandler)
		{
			await ParallelAsync.ForAsync(0, 10000, (i, s) => WriteAsync(i, s), asyncHandler).ConfigureAwait(false);
			await ParallelAsync.ForAsync(10000, 20000, (i, s) => WriteAsync(i, s), asyncHandler).ConfigureAwait(false);

			//writer.Fault(new Exception("Stop!"));

			await Task.Delay(1);
			await ParallelAsync.ForAsync(20000, 100000, (i, s) => WriteAsync(i, s), asyncHandler).ConfigureAwait(false);

			await Task.Delay(1000); // Demonstrate that when nothing buffered the active stream closes.
			await ParallelAsync.ForAsync(100000, 1000000, (i, s) => WriteAsync(i, s), asyncHandler).ConfigureAwait(false);

			//Console.WriteLine("Total Posted: {0:#,##0}", count);
		}

		static async Task WriteAsync(int i, Func<string, Task> asyncHandler)
		{
			var message = $"{i}) {DateTime.Now}\n 00000000000000000000000000000000111111111111111111111111111222222222222222222222222222";
			var t = Stopwatch.StartNew();
			await asyncHandler(message).ConfigureAwait(false);
			t.Stop();
			Counter.Add(new Telemetry() { Time = t.Elapsed, Bytes = message.Length });
			Interlocked.Increment(ref count);
		}

		static async Task Dump<T>(ISourceBlock<T> source, ITargetBlock<T> target)
		{
			using (source.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true }))
			{
				source.Complete();
				await source.Completion.ConfigureAwait(false);
			}
			await target.Completion.ConfigureAwait(false);
		}
	}

	static class ParallelAsync
	{
		public static Task ForAsync<TState>(int fromInclusive, int toExclusive, Func<int, TState, Task> bodyAsync, TState state)
		{
			int procCount = Environment.ProcessorCount;
			int groupSize = (toExclusive - fromInclusive) / procCount;

			List<Task> tasks = new List<Task>();
			for (int p = 0; p < procCount; p++)
			{
				var start = fromInclusive + groupSize * p;
				var end = p == procCount - 1 ? toExclusive : fromInclusive + groupSize * (p + 1);
				tasks.Add(Task.Run(() => ForAsyncPartition(start, end, bodyAsync, state)));
			}

			return Task.WhenAll(tasks);
		}

		private static async Task ForAsyncPartition<TState>(int fromInclusive, int toExclusive, Func<int, TState, Task> bodyAsync, TState state)
		{
			for (var i = fromInclusive; i < toExclusive; i++)
				await bodyAsync(i, state).ConfigureAwait(false);
		}
	}
}
