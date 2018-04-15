using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Open
{
	public class AsyncFileWriter : IDisposableAsync, ITargetBlock<byte[]>, ITargetBlock<char[]>, ITargetBlock<string>
	{
		public readonly string FilePath;
		public readonly int BoundedCapacity;
		public readonly Encoding Encoding;
		public readonly FileShare FileShareMode;

		readonly Channel<byte[]> _channel;
		readonly CancellationTokenSource _canceller;


		#region Constructors
		/// <summary>
		/// Constructs an AsyncFileWriter for consuming bytes from multiple threads and appending to a single file.
		/// </summary>
		/// <param name="filePath">The file system path for the file to open and append to.</param>
		/// <param name="boundedCapacity">The maximum number of entries to allow before blocking producing threads.</param>
		/// <param name="encoding">The encoding type to use for transforming strings and characters to bytes.  The default is UTF8.</param>
		/// <param name="fileSharingMode">The file sharing mode to use.  The default is FileShare.None (will not allow multiple writers). </param>
		public AsyncFileWriter(string filePath, int boundedCapacity, Encoding encoding = null, FileShare fileSharingMode = FileShare.None)
		{
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			BoundedCapacity = boundedCapacity;
			Encoding = encoding ?? Encoding.UTF8;
			FileShareMode = fileSharingMode;

			_channel = Channel.CreateBounded<byte[]>(boundedCapacity);
			_canceller = new CancellationTokenSource();
			Completion = ProcessBytesAsync(_canceller.Token);
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
			_channel.Writer.TryComplete();
			return Completion;
		}
		#endregion

		async Task ProcessBytesAsync(CancellationToken token)
		{
			var reader = _channel.Reader;
			using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShareMode, bufferSize: 4096 * 4, useAsync: true))
			{
				Task writeTask = Task.CompletedTask;
				while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
				{
					while (reader.TryRead(out byte[] bytes))
					{
						token.ThrowIfCancellationRequested();
						await writeTask.ConfigureAwait(false);
						writeTask = fs.WriteAsync(bytes, 0, bytes.Length);
					}
				}
				await writeTask.ConfigureAwait(false);
			}
		}

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
			if (_disposeCalled) throw new ObjectDisposedException(GetType().ToString());
			if (bytes == null) throw new ArgumentNullException(nameof(bytes));
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

		#region IDisposable Support
		private bool _disposeCalled = false;
		private bool _disposed = false; // To detect redundant calls


		protected virtual async Task DisposeAsync(bool disposing)
		{
			if (!_disposed)
			{
				_disposeCalled = true;

				if (disposing)
				{
					await Complete().ConfigureAwait(false);
				}
				else
				{
					_channel.Writer.TryComplete();
					_canceller.Cancel();
				}

				_disposed = true;
			}
		}

		~AsyncFileWriter()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			DisposeAsync(false).Wait();
		}

		/// <summary>
		/// Signals completion and waits for all bytes to be written to the destination.
		/// If immediately cancellation of activity is required, call .CompleteImmediate() before disposing.
		/// </summary>
		public async Task DisposeAsync()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			await DisposeAsync(true).ConfigureAwait(false);
			// TODO: uncomment the following line if the finalizer is overridden above.
			GC.SuppressFinalize(this);
		}

		#endregion

		#region ITargetBlock

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] bytes, ISourceBlock<byte[]> source, bool consumeToAccept)
		{
			if (_channel.Writer.TryWrite(bytes))
			{
				return DataflowMessageStatus.Accepted;
			} else
			{
				return DataflowMessageStatus.Declined;
			}
		}

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, char[] messageValue, ISourceBlock<char[]> source, bool consumeToAccept)
			=> OfferMessage(messageHeader, Encoding.GetBytes(messageValue), null, false);

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, string messageValue, ISourceBlock<string> source, bool consumeToAccept)
			=> OfferMessage(messageHeader, Encoding.GetBytes(messageValue), null, false);

		void IDataflowBlock.Complete()
		{
			this.Complete();
		}

		public void Fault(Exception exception)
		{
			throw new NotImplementedException();
		}


		#endregion
	}
}