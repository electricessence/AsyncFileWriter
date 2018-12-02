using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AsyncFileWriterTester
{
	static class ParallelAsync
	{
		public static Task ForAsync(int fromInclusive, int toExclusive, Func<int, Task> bodyAsync)
		{
			int procCount = Environment.ProcessorCount;
			int groupSize = (toExclusive - fromInclusive) / procCount;

			var tasks = new List<Task>();
			for (int p = 0; p < procCount; p++)
			{
				var start = fromInclusive + groupSize * p;
				var end = p == procCount - 1 ? toExclusive : fromInclusive + groupSize * (p + 1);
				tasks.Add(Task.Run(() => ForAsyncPartition(start, end, bodyAsync)));
			}

			return Task.WhenAll(tasks);
		}

		private static async Task ForAsyncPartition(int fromInclusive, int toExclusive, Func<int, Task> bodyAsync)
		{
			for (var i = fromInclusive; i < toExclusive; i++)
				await bodyAsync(i);
		}
	}
}
