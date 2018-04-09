using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Open
{
	public class AsyncFileWriter
		: ITargetBlock<byte[]>, ITargetBlock<char[]>, ITargetBlock<string>, IDisposable
	{
		public readonly string FilePath;
		public readonly Encoding Encoding;
		readonly ActionBlock<byte[]> _writer;
		Lazy<FileStream> _fileStream;
		bool _completeCalled; // Need to allow for postponed messages to be processed.

		#region Constructors
		AsyncFileWriter(string filePath,
			Encoding encoding,
			ExecutionDataflowBlockOptions options = null)
		{
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			Encoding = encoding ?? Encoding.UTF8;

			_writer = new ActionBlock<byte[]>(async bytes =>
			{
				await GetFileStream().Value.WriteAsync(bytes, 0, bytes.Length);
				if (_writer.InputCount == 0) OnWriterEmpty();
			},
			options);

			_writer.Completion
				.ContinueWith(OnWriterEmpty);
		}

		public AsyncFileWriter(string filePath, Encoding encoding = null, TaskScheduler taskScheduler = null, int boundedCapacity = DataflowBlockOptions.Unbounded)
		: this(filePath, encoding, new ExecutionDataflowBlockOptions
		{
			TaskScheduler = taskScheduler,
			BoundedCapacity = boundedCapacity
		})
		{ }

		public AsyncFileWriter(string filePath, TaskScheduler taskScheduler, int boundedCapacity = DataflowBlockOptions.Unbounded)
		: this(filePath, null, taskScheduler, boundedCapacity) { }

		public AsyncFileWriter(string filePath, Encoding encoding, int boundedCapacity)
		: this(filePath, encoding, null, boundedCapacity) { }

		public AsyncFileWriter(string filePath, int boundedCapacity)
		: this(filePath, null, null, boundedCapacity) { }
		#endregion

		#region Completion
		/// <summary>
		/// The task that indicates completion.
		/// </summary>
		public Task Completion => _writer.Completion;

		/// <summary>
		/// Signals that no more bytes should be accepted and signal completion once all the bytes have been written.
		/// </summary>
		public void Complete()
		{
			_completeCalled = true;
			// Avoid tossing out postponed...
			if (_writer.InputCount == 0)
				_writer.Complete();
		}

		/// <summary>
		/// Stops all processing of bytes and signals completion.
		/// </summary>
		public void CompleteImmediate()
		{
			Complete();
			_writer.Complete();
		}
		#endregion

		Lazy<FileStream> GetFileStream()
		{
			return LazyInitializer.EnsureInitialized(ref _fileStream, () => new Lazy<FileStream>(() =>
			{
				Debug.WriteLine($"Initializing FileStream: {FilePath}");
				return new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true);
			}));
		}

		void OnWriterEmpty(Task task = null)
		{
			if (_completeCalled)
				_writer.Complete();

			var fs = Interlocked.Exchange(ref _fileStream, null);
			if (fs?.IsValueCreated ?? false)
			{
				Debug.WriteLine($"Disposing FileStream: {FilePath}");
				fs.Value.Dispose(); // Just in case...
			}
		}

		#region OfferMessage
		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, byte[] messageValue, ISourceBlock<byte[]> source, bool consumeToAccept)
			=> _completeCalled
			? DataflowMessageStatus.DecliningPermanently
			: ((ITargetBlock<byte[]>)_writer).OfferMessage(messageHeader, messageValue, source, consumeToAccept);

		// Might be able to build a proxy to translate the source block to the proper values and allow consumption.
		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, char[] messageValue, ISourceBlock<char[]> source, bool consumeToAccept)
			=> OfferMessage(messageHeader, Encoding.GetBytes(messageValue), null, false);

		public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, string messageValue, ISourceBlock<string> source, bool consumeToAccept)
			=> OfferMessage(messageHeader, Encoding.GetBytes(messageValue), null, false);

		#endregion

		public void Fault(Exception exception)
		{
			((ITargetBlock<byte[]>)_writer).Fault(exception);
		}

		public bool PostLine(string line = null)
			=> this.Post((line ?? string.Empty) + '\n');

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			Complete();
			if (!disposedValue)
			{
				if (!disposing) // Garbage collecting?
				{
					_writer.Complete();
				}

				_writer.Completion
					.ContinueWith(t => { /* Ignore fault */ })
					.Wait();

				disposedValue = true;
			}
		}

		~AsyncFileWriter()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(false);
		}

		/// <summary>
		/// Signals completion and waits for all bytes to be written to the destination.
		/// If immediately cancellation of activity is required, call .CompleteImmediate() before disposing.
		/// </summary>
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}