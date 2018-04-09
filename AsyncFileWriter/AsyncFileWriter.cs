using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Open
{
    public class AsyncFileWriter : IDisposable
    {
        public readonly string FilePath;
        public readonly Encoding Encoding;
        public readonly int BoundedCapacity;

        readonly Channel<byte[]> _channel;
        readonly CancellationTokenSource _canceller;


        #region Constructors
        public AsyncFileWriter(string filePath, int boundedCapacity, Encoding encoding = null)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Encoding = encoding ?? Encoding.UTF8;
            BoundedCapacity = boundedCapacity;

            _canceller = new CancellationTokenSource();
            _channel = Channel.CreateBounded<byte[]>(boundedCapacity);
            Completion = ProcessBytes();
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

        async Task ProcessBytes()
        {
            var token = _canceller.Token;
            while (await _channel.Reader.WaitToReadAsync(token))
            {
                //Debug.WriteLine($"Initializing FileStream: {FilePath}");
                using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true))
                {
                    while (_channel.Reader.TryRead(out byte[] bytes))
                    {
                        token.ThrowIfCancellationRequested();
                        fs.Write(bytes, 0, bytes.Length);
                    }

                    //Debug.WriteLine($"Disposing FileStream: {FilePath}");
                }
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
        public void Add(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            while (!_channel.Writer.TryWrite(bytes))
                AssertWritable(_channel.Writer.WaitToWriteAsync().Result);
        }

        /// <summary>
        /// Queues characters for writing to the file.
        /// If the .Complete() method was called, this will throw an InvalidOperationException.
        /// </summary>
        public void Add(char[] characters)
        {
            if (characters == null) throw new ArgumentNullException(nameof(characters));
            Add(Encoding.GetBytes(characters));
        }

        /// <summary>
        /// Queues a string for writing to the file.
        /// If the .Complete() method was called, this will throw an InvalidOperationException.
        /// </summary>
        public void Add(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Add(Encoding.GetBytes(value));
        }

        /// <summary>
        /// Queues a string for writing to the file suffixed with a newline character.
        /// If the .Complete() method was called, this will throw an InvalidOperationException.
        /// </summary>
        public void AddLine(string line = null)
            => Add((line ?? string.Empty) + '\n');

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Complete().Wait();
                }
                else
                {
                    _channel.Writer.TryComplete();
                    _canceller.Cancel();
                }

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