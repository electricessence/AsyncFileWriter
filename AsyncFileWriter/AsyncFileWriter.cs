using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace AsyncFileWriter
{
    public class AsyncFileWriter
    {
        public readonly string FilePath;
        readonly BufferBlock<string> _buffer;
        ActionBlock<string> _writer;

        public AsyncFileWriter(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _buffer = new BufferBlock<string>();
        }

        public void Write(string value)
        {
            _buffer.Post(value);
            EnsureWriter();
        }

        void EnsureWriter()
        {
            if (_buffer.Count != 0)
            {
                LazyInitializer.EnsureInitialized(ref _writer, () =>
                {
                    var fs = File.AppendText(FilePath);
                    var writer = new ActionBlock<string>(fs.WriteAsync);
                    writer.Completion.ContinueWith(t =>
                    {
                        fs.Dispose();
                        Interlocked.Exchange(ref _writer, null);
                        EnsureWriter();
                    });

                    Task.Run(() =>
                    {
                        while(_buffer.TryReceiveAll(out IList<string> entries))
                        {
                            foreach (var e in entries)
                                writer.Post(e);
                        }

                        // No more for now... 
                        writer.Complete();
                    });

                    return writer;
                });
            }
        }

        public void WriteLine(string value)
        {
            Write((value ?? string.Empty) + '\n');
        }
    }
}
