using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Open
{
	public class AsyncFileWriter : IDisposable
	{
		public readonly string FilePath;
		public readonly Encoding Encoding;
		readonly Channel<byte[]> _channel;
		Lazy<FileStream> _fileStream;
		bool _completeCalled; // Need to allow for postponed messages to be processed.

		#region Constructors
		public AsyncFileWriter(string filePath, int boundedCapacity, Encoding encoding = null)
		{
			FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
			Encoding = encoding ?? Encoding.UTF8;

			_channel = Channel.CreateBounded<byte[]>(boundedCapacity);
			var completion = _channel.Reader.Completion;
			completion.ContinueWith(OnCompletion);
			Completion = completion;

			_writer = new ActionBlock<byte[]>(async bytes =>
			{
				await GetFileStream().Value.WriteAsync(bytes, 0, bytes.Length);
				if (_writer.InputCount == 0) OnWriterEmpty();
			},
			options);

			_writer.Completion
				.ContinueWith(OnWriterEmpty);
		}

		#endregion

		#region Completion
		/// <summary>
		/// The task that indicates completion.
		/// </summary>
		public Task Completion { get; private set; }

		/// <summary>
		/// Signals that no more bytes should be accepted and signal completion once all the bytes have been written.
		/// </summary>
		public void Complete() => _channel.Writer.TryComplete();
		#endregion

		Lazy<FileStream> GetFileStream()
		{
			return LazyInitializer.EnsureInitialized(ref _fileStream, () => new Lazy<FileStream>(() =>
			{
				Debug.WriteLine($"Initializing FileStream: {FilePath}");
				return new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true);
			}));
		}

		void OnCompletion(Task task = null)
		{
			var fs = Interlocked.Exchange(ref _fileStream, null);
			if (fs?.IsValueCreated ?? false)
			{
				Debug.WriteLine($"Disposing FileStream: {FilePath}");
				fs.Value.Dispose(); // Just in case...
			}
		}

		public async ValueTask<bool> WriteAsync(byte[] bytes)
		{
			while (!_channel.Writer.TryWrite(bytes))
			{
				if (!await _channel.Writer.WaitToWriteAsync())
					return false;
			}
			return true;
		}

		public bool Write(byte[] bytes)
		{
			while(!_channel.Writer.TryWrite(bytes))
			{
				if (!_channel.Writer.WaitToWriteAsync().Result)
					return false;
			}
			return true;
		}

		public bool Write(char[] characters)
			=> _channel.Writer.TryWrite(Encoding.GetBytes(characters));

		public bool Write(string value)
			=> _channel.Writer.TryWrite(Encoding.GetBytes(value));

		public bool WriteLine(string line = null)
			=> Write((line ?? string.Empty) + '\n');

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				Complete();
				Completion
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