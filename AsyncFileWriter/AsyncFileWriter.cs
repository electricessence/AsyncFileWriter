using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Open.Threading
{
	public sealed class AsyncFileWriter
		: IDisposable, ITargetBlock<byte[]>
	{
		public readonly string FilePath;
		public readonly int BoundedCapacity;
		public readonly FileShare FileShareMode;
		public readonly bool AsyncFileStream;
		public readonly bool AsyncFileWrite;
		public readonly int BufferSize;

		bool _declinePermanently;
		readonly Channel<ReadOnlyMemory<byte>> _channel;

		#region Constructors
		/// <summary>
		/// Constructs an AsyncFileWriter for consuming bytes from multiple threads and appending to a single file.
		/// </summary>
		/// <param name="filePath">The file system path for the file to open and append to.</param>
		/// <param name="boundedCapacity">The maximum number of entries to allow before blocking producing threads.</param>
		/// <param name="fileSharingMode">The file sharing mode to use.  The default is FileShare.None (will not allow multiple writers). </param>
		/// <param name="bufferSize">The buffer size to use with the underlying FileStreams.  The default is 4KB. </param>
		/// <param name="asyncFileStream">If true, sets the underlying FileStreams to use async mode.</param>\
		/// <param name="asyncFileWrite">If true, uses a fully asynchronous write scheme.</param>
		public AsyncFileWriter(
			string filePath,
			int boundedCapacity,
			FileShare fileSharingMode = FileShare.None,
			int bufferSize = 4096,
			bool asyncFileStream = false,
			bool asyncFileWrite = false,
			CancellationToken cancellationToken = default)
		{
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			BoundedCapacity = boundedCapacity;

			FileShareMode = fileSharingMode;
			BufferSize = bufferSize;
			AsyncFileStream = asyncFileStream;
			AsyncFileWrite = asyncFileWrite;

			_channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(boundedCapacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true
			});

			Completion = ProcessBytesAsync(cancellationToken);
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

		async Task ProcessBytesAsync(CancellationToken cancellationToken)
		{
			try
			{
				await ProcessBytesAsyncCore(cancellationToken);
			}
			catch(Exception ex)
			{
				_channel.Writer.TryComplete(ex);
				throw;
			}

			await _channel.Reader.Completion;
		}

		async Task ProcessBytesAsyncCore(CancellationToken cancellationToken)
		{
			var reader = _channel.Reader;
			while (await reader.WaitToReadAsync(cancellationToken))
			{
				using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShareMode, bufferSize: BufferSize, useAsync: AsyncFileStream))
				{
					if (AsyncFileWrite)
					{
						ValueTask writeTask = new ValueTask(Task.CompletedTask);
						while (!cancellationToken.IsCancellationRequested
							&& reader.TryRead(out ReadOnlyMemory<byte> bytes))
						{
							await writeTask;
							writeTask = fs.WriteAsync(bytes, cancellationToken);
						}

						await writeTask;
					}
					else
					{
						while (!cancellationToken.IsCancellationRequested
							&& reader.TryRead(out ReadOnlyMemory<byte> bytes))
						{
							fs.Write(bytes.Span);
						}
					}

					// FlushAsync here rather than block in Dispose on Flush
					await fs.FlushAsync(cancellationToken);
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

		void AssertStillAccepting()
		{
			if (_disposer != null) throw new ObjectDisposedException(GetType().ToString());
			AssertWritable(!_declinePermanently);
		}

		public bool TryAdd(ReadOnlyMemory<byte> bytes)
			=> _channel.Writer.TryWrite(bytes);

		/// <summary>
		/// Queues bytes for writing to the file.
		/// If the .Complete() method was called, this will throw an InvalidOperationException.
		/// </summary>
		public Task AddAsync(ReadOnlyMemory<byte> bytes)
			=> _channel.Writer.TryWrite(bytes)
				? Task.CompletedTask
				: AddAsyncCore(bytes);

		async Task AddAsyncCore(ReadOnlyMemory<byte> bytes)
		{
			do
			{
				AssertStillAccepting();

				// Retry?
				AssertWritable(
					await _channel.Writer.WaitToWriteAsync()
				);
			}
			while (!_channel.Writer.TryWrite(bytes));
		}

		/// <summary>
		/// Queues bytes for writing to the file.
		/// If the .Complete() method was called, this will throw an InvalidOperationException.
		/// </summary>
		public void Add(ReadOnlyMemory<byte> bytes)
		{
			if (!_channel.Writer.TryWrite(bytes))
				AddAsyncCore(bytes).Wait();
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
						if (!_channel.Reader.Completion.IsCompleted)
							_channel.Writer.TryComplete(new ObjectDisposedException(GetType().ToString()));
						// Not sure what legitimately else can be done here.  Faulting should stop the task.

						c = c.ContinueWith(t => { /* Avoid exceptions propagated to the GC. */ });
					}
					return c;
				})).Value;

		public async Task DisposeAsync()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			await DisposeAsync(true);
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
