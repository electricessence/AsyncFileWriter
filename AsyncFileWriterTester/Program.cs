using System;
using System.Threading.Tasks;

namespace AsyncFileWriterTester
{
	class Program
	{
		static async Task Main(string[] args)
		{
			Console.WriteLine("TESTS WITH PARTIAL BLOCKING:\n");

			// The simplest benchmark using lock() and synchronous file writes.
			await SynchronousTester.TestSynchronizedFileStream();

			await SynchronousTester.TestAsyncFileWriter(100000);
			await SynchronousTester.TestAsyncFileWriter(10000);
			await SynchronousTester.TestAsyncFileWriter(1000);
			await SynchronousTester.TestAsyncFileWriter(100);
			// await SynchronousTester.TestMultipleFileStreams(); // This simply takes way too long to even report.

			Console.WriteLine();
			Console.WriteLine("FULLY ASYNCHRONOUS:\n");

			await AsyncTester.TestAsyncFileWriter(100000);
			await AsyncTester.TestAsyncFileWriter(10000);
			await AsyncTester.TestAsyncFileWriter(1000);
			await AsyncTester.TestAsyncFileWriter(100);

			Console.WriteLine("Press ENTER to continue.");
			Console.ReadLine();
		}
	}
}
