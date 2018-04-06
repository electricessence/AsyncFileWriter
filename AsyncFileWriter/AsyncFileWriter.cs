using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Open
{
	public class AsyncFileWriter : ITargetBlock<byte[]>, IDisposable
	{
		public readonly string FilePath;
		readonly BufferBlock<byte[]> _buffer;
		Task<Task> _writerCompletion;

		public AsyncFileWriter(string filePath)
		{
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			_buffer = new BufferBlock<byte[]>();
			Completion = CreateCompletion();
		}

		public void Dispose()
		{
			Complete();
			Completion.ContinueWith(t => { /* Ignore fault */ }).Wait();
		}

		// Allow propagation of faults.
		Task CreateCompletion()
			=> _buffer
			.Completion
			.ContinueWith(
				t1 => _writerCompletion
					?.Unwrap()
					?.ContinueWith(t2 => _buffer.Completion).Unwrap()
					?? _buffer.Completion)
			.Unwrap();

		public void Complete() => _buffer.Complete();

		public Task Completion { get; private set; }

		Task InitWriter()
		{
			FileStream fs = null;
			try
			{
				Debug.WriteLine($"Initializing FileStream: {FilePath}");
				fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true);

				IDisposable link = null;
				ActionBlock<byte[]> writer = null;
				writer = new ActionBlock<byte[]>(async bytes =>
				{
					await fs.WriteAsync(bytes, 0, bytes.Length);
					if (_buffer.Count == 0 && writer.InputCount == 0)
					{
						link.Dispose(); // Unlink immediately...
						writer.Complete();
					}
				});

				link = _buffer.LinkTo(writer, new DataflowLinkOptions { PropagateCompletion = true }); // Begin consumption immediately...

				return writer.Completion.ContinueWith(t =>
				{
					link.Dispose();
					if (t.IsFaulted) Fault(t.Exception);
					Debug.WriteLine($"Disposing FileStream. {FilePath}");
					fs.Dispose();
					Interlocked.Exchange(ref _writerCompletion, null);
					EnsureWriter();
				});
			}
			catch
			{
				Debug.WriteLine($"Initializing FileStream FAILED");
				fs?.Dispose();
				Interlocked.Exchange(ref _writerCompletion, null); // Allow for retrying...
				throw;
			}
		}

		void EnsureWriter()
		{
			if (_buffer.Count != 0)
			{
				Task<Task> t = null;
				var task = LazyInitializer.EnsureInitialized(ref _writerCompletion,
					() => t = new Task<Task>(InitWriter));
				if (task == t) task.Start();  // Are we the thread/task that is the creator?
			}
		}

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
		{
			var status = ((ITargetBlock<byte[]>)_buffer).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
			if (status == DataflowMessageStatus.Accepted) EnsureWriter();
			return status;
		}

		public void Fault(Exception exception)
		{
			((ITargetBlock<byte[]>)_buffer).Fault(exception);
		}
	}
}