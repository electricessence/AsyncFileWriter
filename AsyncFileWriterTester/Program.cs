using System;
using System.Threading.Tasks;

namespace AsyncFileWriterTester
{
	class Program
	{
		static async Task Main(string[] args)
		{
			var capacities = new int[] { /*10000000, 1000000,*/ 150000, 100000, 50000, 10000/*, 1000, 500, 100*/ };

			Console.WriteLine("STANDARD BENCHMARKS:\n");

			await SynchronousTester.TestFileStreamSingleThread();
			// await SynchronousTester.TestAsyncFileStreamSingleThread(); // Currently async writes to files are broken, test after fix. .NET Core 2.2 possbily
			await SynchronousTester.TestSynchronizedFileStream(); // The simplest benchmark using lock() and synchronous file writes.

			Console.WriteLine("TESTS WITH PARTIAL BLOCKING:\n");


			foreach (var c in capacities)
			{
				await SynchronousTester.TestBlockingCollection(c);
				await SynchronousTester.TestAsyncFileWriter(c);
			}

			// await SynchronousTester.TestMultipleFileStreams(); // This simply takes way too long to even report.


			//Console.WriteLine();
			//Console.WriteLine("TESTS WITH PARTIAL BLOCKING AND ASYNC FILESTREAM:\n");

			//foreach (var c in capacities)
			//	await AsyncTester.TestAsyncFileWriter(c, false);

			Console.WriteLine();
			Console.WriteLine("FULLY ASYNCHRONOUS:\n");

			foreach (var c in capacities)
				await AsyncTester.TestAsyncFileWriter(c, true);


			Console.WriteLine("Press ENTER to continue.");
			Console.ReadLine();
		}
	}
}
