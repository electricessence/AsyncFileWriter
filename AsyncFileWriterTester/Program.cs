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
            Test(1000000);
            Test(100000);
            Test(10000);
            Test(1000);

            Console.WriteLine("Press ENTER to continue.");
            Console.ReadLine();
        }

        static void Test(int boundedCapacity = -1)
        {
            Console.WriteLine(boundedCapacity < 0 ? "Starting unbounded test" : $"Starting max {boundedCapacity}");
            var dir = Environment.CurrentDirectory;
            var filePath = Path.Combine(dir, "AsyncFileWriterTest.txt");
            File.Delete(filePath); // Start from scratch. (comment out for further appends.)

            var byteCounter = new ConcurrentBag<int>();
            var timeCounter = new ConcurrentBag<TimeSpan>();

            var sw = Stopwatch.StartNew();
            Console.WriteLine($"Writing to file: {filePath}");
            using (var writer = new AsyncFileWriter(filePath, boundedCapacity))
            {
                int count = 0;
                void write(int i)
                {
                    var message = $"{i}) {DateTime.Now}\n";
                    var t = Stopwatch.StartNew();
                    writer.Add(message);
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

                Console.WriteLine($"Total Posted: {count}");
            }

            Console.WriteLine($"Total Time: {sw.Elapsed.TotalSeconds} seconds");
            Console.WriteLine($"Total Bytes: {byteCounter.Sum()}");
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
