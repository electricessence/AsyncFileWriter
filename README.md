# AsyncFileWriter
Manages multi-threaded writing to a single file.

## Async Options
```.AsyncFileStream``` and ```.AsyncFileWrite``` are provided for configurability but the defaultof ```false``` for both is recommended.

## Default Behavior

Testing has revealed that using a standard ```fs.Write(bytes)``` on the underlying file stream yields optimal results.

```await fs.WriteAsync(bytes)``` creates enough overhead that the overall time taken to write to the destination can be much worse.
This may be fixed in future versions of .NET Core/Standard.
