using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncFileWriterTester
{
	public static class SourceBuilder
	{
		public static readonly IReadOnlyList<ReadOnlyMemory<byte>> Source;
		static SourceBuilder()
		{
			var list = new List<ReadOnlyMemory<byte>>();
			for (var i = 0; i < 1000000; i++)
			{
				var text = $"{i}) {DateTime.Now} 00000000000000000000000000000000111111111111111111111111111222222222222222222222222222\n";
				var message = Encoding.UTF8.GetBytes(text);
				list.Add(message.AsMemory());
			}

			Source = list.AsReadOnly();
		}

	}
}
