// PowerFlow samples — run all three scenarios sequentially.
// Each sample is self-contained; read them in order or jump to the one you need.

Console.WriteLine("=== PowerFlow Sample Scenarios ===");
Console.WriteLine();

Sample01_BasicAcSolve.Run();
Sample02_DistributedSlack.Run();
Sample03_DcWarmStart.Run();
