using Open;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AsyncFileWriterTester
{
	class Program
	{
		static void Main(string[] args)
		{
			var dir = Environment.CurrentDirectory;
			var fileName = args.FirstOrDefault() ?? "AsyncFileWriterTest.txt";
			var filePath = Path.Combine(dir, fileName);
			File.Delete(filePath); // Start from scratch. (comment out for further appends.)

			Console.WriteLine($"Writing to file: {filePath}");
			using (var writer = new AsyncFileWriter(filePath))
			{

				// Test to ensure blocks are linkable and properly pass messages.
				var buffer = new TransformBlock<string, byte[]>(
					value=>Encoding.UTF8.GetBytes(value +'\n'),
					new ExecutionDataflowBlockOptions {
						BoundedCapacity = 200000 // Test a max pre-buffered amount here.
					});

				buffer.LinkTo(writer, new DataflowLinkOptions { PropagateCompletion = true });
				writer.Completion.ContinueWith(t => {
					buffer.Complete();
					buffer.LinkTo(DataflowBlock.NullTarget<byte[]>()); // Empty the buffer and allow the buffer to complete.
				});

				void write(int i)
				{
					var message = $"{i}) {DateTime.Now}";
					if(!buffer.Post(message) && !buffer.Completion.IsCompleted)
						buffer.SendAsync(message).Wait();
				}

				Parallel.For(0, 10000, write);
				Parallel.For(10000, 20000, write);
		
				//writer.Fault(new Exception("Stop!"));

				Task.Delay(1).Wait();
				Parallel.For(20000, 100000, write);

				Task.Delay(1000).Wait(); // Demonstrate that when nothing buffered the active stream closes.
				Parallel.For(100000, 1000000, write);

				buffer.Complete();
				buffer.Completion.Wait();

				if (writer.Completion.IsFaulted)
					throw writer.Completion.Exception;
			}
		}
	}
}
