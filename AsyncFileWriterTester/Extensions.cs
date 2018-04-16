using System;

namespace AsyncFileWriterTester
{
	public static class Extensions
    {
		public static void EmitToConsole(this (int TotalBytesQueued, TimeSpan AggregateTimeWaiting, TimeSpan Elapsed) run)
		{
			Console.WriteLine("Total Time: {0} seconds", run.Elapsed.TotalSeconds);
			Console.WriteLine("Total Bytes: {0:#,##0}", run.TotalBytesQueued);
			Console.WriteLine("Aggregate Waiting: {0}", run.AggregateTimeWaiting);
			Console.WriteLine("------------------------");
			Console.WriteLine();
		}
    }
}
