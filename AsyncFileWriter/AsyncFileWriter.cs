using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Open
{
	public sealed class AsyncFileWriter
		: IDisposable, ITargetBlock<byte[]>, ITargetBlock<char[]>, ITargetBlock<string>
	{
		public readonly string FilePath;
		public readonly int BoundedCapacity;
		public readonly Encoding Encoding;
		public readonly FileShare FileShareMode;
		public readonly bool AsyncFileStream;
		public readonly bool AsyncFileWrite;
		public readonly int BufferSize;

		bool _declinePermanently;
		readonly Channel<byte[]> _channel;

		#region Constructors
		/// <summary>
		/// Constructs an AsyncFileWriter for consuming bytes from multiple threads and appending to a single file.
		/// </summary>
		/// <param name="filePath">The file system path for the file to open and append to.</param>
		/// <param name="boundedCapacity">The maximum number of entries to allow before blocking producing threads.</param>
		/// <param name="encoding">The encoding type to use for transforming strings and characters to bytes.  The default is UTF8.</param>
		/// <param name="fileSharingMode">The file sharing mode to use.  The default is FileShare.None (will not allow multiple writers). </param>
		/// <param name="bufferSize">The buffer size to use with the underlying FileStreams.  The default is 4KB. </param>
		/// <param name="asyncFileStream">If true, sets the underlying FileStreams to use async mode.</param>\
		/// <param name="asyncFileWrite">If true, uses a fully asynchronous write scheme.</param>
		public AsyncFileWriter(
			string filePath,
			int boundedCapacity,
			Encoding encoding = null,
			FileShare fileSharingMode = FileShare.None,
			int bufferSize = 4096,
			bool asyncFileStream = false,
			bool asyncFileWrite = false)
		{
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			BoundedCapacity = boundedCapacity;
			Encoding = encoding ?? Encoding.UTF8;
			FileShareMode = fileSharingMode;
			BufferSize = bufferSize;
			AsyncFileStream = asyncFileStream;
			AsyncFileWrite = asyncFileWrite;

			_channel = Channel.CreateBounded<byte[]>(boundedCapacity);
			Completion = ProcessBytesAsync()
				.ContinueWith(
					t => t.IsCompleted
						? _channel.Reader.Completion
						: t)
				.Unwrap(); // Propagate the task state...
		}

		#endregion

		#region Completion
		/// <summary>
		/// The task that indicates completion.
		/// </summary>
		public Task Completion { get; private set; }

		/// <summary>
		/// Signals that no more bytes should be accepted and signal completion once all the bytes have been processed to the destination file.
		/// </summary>
		public Task Complete()
		{
			_declinePermanently = true;
			_channel.Writer.TryComplete();
			return Completion;
		}
		#endregion

		async Task ProcessBytesAsync()
		{
			var reader = _channel.Reader;
			while (await reader.WaitToReadAsync().ConfigureAwait(false))
			{
				using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShareMode, bufferSize: BufferSize, useAsync: AsyncFileStream))
				{
					if (AsyncFileWrite)
					{
						Task writeTask = Task.CompletedTask;
						while (reader.TryRead(out byte[] bytes))
						{
							await writeTask.ConfigureAwait(false);
							writeTask = fs.WriteAsync(bytes, 0, bytes.Length);
						}

						await writeTask.ConfigureAwait(false);
						// FlushAsync here rather than block in Dispose on Flush
						await fs.FlushAsync().ConfigureAwait(false);
					}
					else
					{
						while (reader.TryRead(out byte[] bytes))
						{
							fs.Write(bytes, 0, bytes.Length);
						}
					}
				}
			}
		}

		#region Add (queue) data methods.
		void AssertWritable(bool writing)
		{
			if (!writing)
			{
				if (Completion.IsFaulted)
					throw new InvalidOperationException("Attempting to write to a faulted writer.");
				else
					throw new InvalidOperationException("Attempting to write to a completed writer.");
			}
		}

		/// <summary>
		/// Queues bytes for writing to the file.
		/// If the .Complete() method was called, this will throw an InvalidOperationException.
		/// </summary>
		public async Task AddAsync(byte[] bytes, params byte[][] more)
		{
			if (bytes == null) throw new ArgumentNullException(nameof(bytes));
			Contract.EndContractBlock();

			if (_disposer != null) throw new ObjectDisposedException(GetType().ToString());
			AssertWritable(!_declinePermanently);

			while (!_channel.Writer.TryWrite(bytes))
			{
				var written = await _channel.Writer.WaitToWriteAsync().ConfigureAwait(false);
				AssertWritable(written);
			}

			if (more.Length != 0) foreach (var v in more) await AddAsync(v);
		}

		/// <summary>
		/// Queues characters for writing to the file.
		/// If the .Complete() method was called, this will throw an InvalidOperationException.
		/// </summary>
		public async Task AddAsync(char[] characters, params char[][] more)
		{
			if (characters == null) throw new ArgumentNullException(nameof(characters));
			Contract.EndContractBlock();

			await AddAsync(Encoding.GetBytes(characters));

			if (more.Length != 0) foreach (var v in more) await AddAsync(v);
		}

		/// <summary>
		/// Queues a string for writing to the file.
		/// If the .Complete() method was called, this will throw an InvalidOperationException.
		/// </summary>
		public async Task AddAsync(params string[] values)
		{
			if (values == null) throw new ArgumentNullException(nameof(values));
			Contract.EndContractBlock();

			foreach (var value in values)
			{
				if (value == null) throw new ArgumentNullException(nameof(value));
				await AddAsync(Encoding.GetBytes(value));
			}
		}

		/// <summary>
		/// Queues a string for writing to the file suffixed with a newline character.
		/// If the .Complete() method was called, this will throw an InvalidOperationException.
		/// </summary>
		public async Task AddLineAsync(string line = null, params string[] more)
		{
			await AddAsync((line ?? string.Empty) + '\n');

			if (more.Length != 0) foreach (var v in more) await AddLineAsync(v);
		}
		#endregion

		#region IDisposable Support
		Lazy<Task> _disposer;

		Task DisposeAsync(bool calledExplicitly)
			// EnsureInitialized is optimistic.
			=> LazyInitializer.EnsureInitialized(ref _disposer,
				// Lazy is pessimistic.
				() => new Lazy<Task>(() =>
				{
					var c = Complete();
					if (!calledExplicitly)
					{
						// Being called by the GC.
						if(!_channel.Reader.Completion.IsCompleted)
							_channel.Writer.TryComplete(new ObjectDisposedException(GetType().ToString()));
						// Not sure what legitimately else can be done here.  Faulting should stop the task.

						c = c.ContinueWith(t => { /* Avoid exceptions propagated to the GC. */ });
					}
					return c;
				})).Value;

		public async Task DisposeAsync()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			await DisposeAsync(true).ConfigureAwait(false);
			// TODO: uncomment the following line if the finalizer is overridden above.
			GC.SuppressFinalize(this);
		}

		~AsyncFileWriter()
		{
			DisposeAsync(false).Wait();
		}

		/// <summary>
		/// Completes the underlying queue and finishes writing out the queued bytes.
		/// No further bytes can be added.
		/// </summary>
		public void Dispose()
		{
			DisposeAsync(true).Wait();
		}
		#endregion

		#region ITargetBlock

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] bytes, ISourceBlock<byte[]> source, bool consumeToAccept)
		{
			if (_declinePermanently || _channel.Reader.Completion.IsCompleted)
				return DataflowMessageStatus.DecliningPermanently;

			if (_channel.Writer.TryWrite(bytes))
				return DataflowMessageStatus.Accepted;

			if (consumeToAccept)
			{
				// How to properly implement this to allow .SendAsync(bytes) to work?
			}

			return DataflowMessageStatus.Declined;
		}

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, char[] messageValue, ISourceBlock<char[]> source, bool consumeToAccept)
			=> OfferMessage(messageHeader, Encoding.GetBytes(messageValue), null, false);

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, string messageValue, ISourceBlock<string> source, bool consumeToAccept)
			=> OfferMessage(messageHeader, Encoding.GetBytes(messageValue), null, false);

		void IDataflowBlock.Complete()
		{
			Complete();
		}

		/// <summary>
		/// Attempts to complete the underlying channel with the provided exception. 
		/// </summary>
		/// <param name="exception">The exception to fault with.</param>
		public void Fault(Exception exception)
		{
			if (exception == null) throw new ArgumentNullException(nameof(exception));
			Contract.EndContractBlock();
			_declinePermanently = true;
			_channel.Writer.TryComplete(exception);
		}

		#endregion
	}
}