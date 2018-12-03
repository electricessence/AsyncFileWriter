using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsyncFileWriterTester
{
	public static class SourceBuilder
	{
		public static IEnumerable<ReadOnlyMemory<byte>> GetSource()
			=> Enumerable
				.Range(0, int.MaxValue)
				.Select(GetLine);

		public static ReadOnlyMemory<byte> GetLine(int i)
		{
			var text = $"{i}) {DateTime.Now} 00000000000000000000000000000000111111111111111111111111111222222222222222222222222222\n";
			var message = Encoding.UTF8.GetBytes(text);
			return message;
		}

	}
}
