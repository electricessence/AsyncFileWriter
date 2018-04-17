using System;
using System.Threading.Tasks;

namespace AsyncFileWriterTester
{
	class Program
	{
		static async Task Main(string[] args)
		{
			var capacities = new int[] { 100000, 10000, 1000, 500, 100 };

			Console.WriteLine("STANDARD BENCHMARKS:\n");

			await SynchronousTester.TestFileStreamSingleThread();
			await SynchronousTester.TestAsyncFileStreamSingleThread();
			await SynchronousTester.TestSynchronizedFileStream(); // The simplest benchmark using lock() and synchronous file writes.

			Console.WriteLine("TESTS WITH PARTIAL BLOCKING:\n");


			foreach (var c in capacities)
				await SynchronousTester.TestAsyncFileWriter(c);
			
			// await SynchronousTester.TestMultipleFileStreams(); // This simply takes way too long to even report.


			//Console.WriteLine();
			//Console.WriteLine("TESTS WITH PARTIAL BLOCKING AND ASYNC FILESTREAM:\n");

			//foreach (var c in capacities)
			//	await AsyncTester.TestAsyncFileWriter(c, false);

			//Console.WriteLine();
			//Console.WriteLine("FULLY ASYNCHRONOUS:\n");

			//foreach (var c in capacities)
			//	await AsyncTester.TestAsyncFileWriter(c, true);


			Console.WriteLine("Press ENTER to continue.");
			Console.ReadLine();
		}
	}
}
