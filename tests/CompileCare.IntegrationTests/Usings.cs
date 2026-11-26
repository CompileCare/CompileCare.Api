global using Xunit;

// Integration tests share a database that some fixtures drop and recreate; never run them concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
