using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// dotnet run -c Release --project benchmarks/WolfMark.Benchmarks -- --filter *
//
// The benchmarks run inside this process: BenchmarkDotNet 0.15 does not know .NET 11 yet and cannot build its own
// host project for it ("GetRuntimeVersion not implemented for NotRecognized"). Once it does, the default toolchain
// (a process per benchmark) can come back by removing the job below.
var config = DefaultConfig.Instance.AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
