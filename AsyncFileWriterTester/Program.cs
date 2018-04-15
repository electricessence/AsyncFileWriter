using Open;
using System;
using System.Collections.Concurrent;
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
		static void Main(string[] args)
		{
			//TestSynchronizedFileStream();
			TestAsyncFileWriter(100000);
			TestAsyncFileWriter(10000);
			TestAsyncFileWriter(1000);
			TestAsyncFileWriter(100);
			//TestMultipleFileStreams();

			Console.WriteLine("Press ENTER to continue.");
			Console.ReadLine();
		}

		static void TestSynchronizedFileStream()
		{
			Console.WriteLine($"Synchronized file stream benchmark.");
			Test((filePath, handler) =>
			{
				using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
				using (var sw = new StreamWriter(fs))
				{
					handler(s =>
					{
						lock (sw) sw.Write(s);
					});
				}
			});
		}

		static void TestMultipleFileStreams()
		{
			Console.WriteLine($"Multiple file stream benchmark.");
			Test((filePath, handler) =>
			{
				handler(s =>
				{
					using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Write))
					using (var sw = new StreamWriter(fs))
						sw.Write(s);
				});
			});
		}

		static void TestAsyncFileWriter(int boundedCapacity = -1)
		{
			Console.WriteLine("{0:#,##0} bounded capacity.", boundedCapacity);
			Test((filePath, handler) =>
			{
				using (var writer = new AsyncFileWriter(filePath, boundedCapacity))
					handler(s => writer.AddAsync(s).Wait());
			});
		}

		static void Test(Action<string, Action<Action<string>>> context)
		{
			var dir = Environment.CurrentDirectory;
			var filePath = Path.Combine(dir, "AsyncFileWriterTest.txt");
			File.Delete(filePath); // Start from scratch. (comment out for further appends.)

			var byteCounter = new ConcurrentBag<int>();
			var timeCounter = new ConcurrentBag<TimeSpan>();

			var sw = Stopwatch.StartNew();
			context(filePath, writeHandler =>
			{
				int count = 0;
				void write(int i)
				{
					var message = $"{i}) {DateTime.Now} 00000000000000000000000000000000111111111111111111111111111222222222222222222222222222\n";
					var t = Stopwatch.StartNew();
					writeHandler(message);
					timeCounter.Add(t.Elapsed);
					byteCounter.Add(message.Length);
					Interlocked.Increment(ref count);
				}

				Parallel.For(0, 10000, write);
				Parallel.For(10000, 20000, write);

				//writer.Fault(new Exception("Stop!"));

				Task.Delay(1).Wait();
				Parallel.For(20000, 100000, write);

				Task.Delay(1000).Wait(); // Demonstrate that when nothing buffered the active stream closes.
				Parallel.For(100000, 1000000, write);

				//Console.WriteLine("Total Posted: {0:#,##0}", count);
			});

			Console.WriteLine($"Total Time: {sw.Elapsed.TotalSeconds} seconds");
			Console.WriteLine("Total Bytes: {0:#,##0}", byteCounter.Sum());
			Console.WriteLine($"Total Blocking Time: {timeCounter.Aggregate((b, c) => b + c)}");
			Console.WriteLine("------------------------");
			Console.WriteLine();

		}

		static void Dump<T>(ISourceBlock<T> source, ITargetBlock<T> target)
		{
			using (source.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true }))
			{
				source.Complete();
				source.Completion.Wait();
			}
			target.Completion.Wait();
		}
	}
}
