# AsyncFileWriter
Manages multi-threaded writing to a single file.

## Async Options
```.AsyncFileStream``` and ```.AsyncFileWrite``` are provided for configurability but the defaultof ```false``` for both is recommended.

## Default Behavior

Testing has revealed that using a standard ```fs.Write(bytes)``` on the underlying file stream yields optimal results.

```await fs.WriteAsync(bytes)``` creates enough overhead that the overall time taken to write to the destination can be much worse.
This may be fixed in future versions of .NET Core/Standard.

---

## Current Testing Results
Total bytes written per test: 116,888,890

### STANDARD BENCHMARKS:

#### File stream standard benchmark.
```
Total Elapsed Time: 0.1750803 seconds
```

#### File stream async benchmark.
```
Total Elapsed Time: 3.1865426 seconds
```

#### Synchronized file stream benchmark.
This test and the following tests use the same test harness and contain +1 second delay time for tesing a full open close and reopen of the underlying file stream.  This is the minimum performance required to meet expecations.  If a test result does not exceed this one's performance (less total time) then it's probably not worth pursuing.

```
Total Time: 3.3883187 seconds
Aggregate Waiting: 00:00:30.8530035
```

### TESTS WITH PARTIAL BLOCKING:

#### 100,000 bounded capacity.
```
Total Time: 2.5057271 seconds
Aggregate Waiting: 00:00:25.4319344
```

#### 10,000 bounded capacity.
```
Total Time: 2.6683573 seconds
Aggregate Waiting: 00:00:29.6922757
```

#### 1,000 bounded capacity.
```
Total Time: 2.5546364 seconds
Aggregate Waiting: 00:00:54.2961971
```

#### 500 bounded capacity.
```
Total Time: 2.9537176 seconds
Aggregate Waiting: 00:00:39.4386377
```

#### 100 bounded capacity.
```
Total Time: 85.4482081 seconds
Aggregate Waiting: 02:18:51.5013343
```


### TESTS WITH PARTIAL BLOCKING AND ASYNC FILESTREAM:

#### 100,000 bounded capacity.
```
Total Time: 6.5296874 seconds
Aggregate Waiting: 00:00:27.9669792
```

#### 10,000 bounded capacity.
```
Total Time: 6.7625757 seconds
Aggregate Waiting: 00:00:31.8351860
```

#### 1,000 bounded capacity.
```
Total Time: 10.807509 seconds
Aggregate Waiting: 00:01:01.5740778
```

#### 500 bounded capacity.
```
Total Time: 8.198802 seconds
Aggregate Waiting: 00:00:41.9564596
```

#### 100 bounded capacity.
```
Total Time: 24.0677165 seconds
Aggregate Waiting: 00:02:27.8041755
```


### FULLY ASYNCHRONOUS:

#### 100,000 bounded capacity.
```
Total Time: 44.2459905 seconds
Aggregate Waiting: 00:04:40.2752856
```

#### 10,000 bounded capacity.
```
Total Time: 55.6650965 seconds
Aggregate Waiting: 00:06:13.0226689
```

#### 1,000 bounded capacity.
```
Total Time: 53.0426809 seconds
Aggregate Waiting: 00:05:50.5497056
```

#### 500 bounded capacity.
```
Total Time: 57.8209485 seconds
Aggregate Waiting: 00:06:24.1047755
```

#### 100 bounded capacity.
```
Total Time: 61.7946165 seconds
Aggregate Waiting: 00:06:49.5865838
```
