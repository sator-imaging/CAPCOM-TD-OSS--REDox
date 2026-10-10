using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = DefaultConfig.Instance.WithOptions(ConfigOptions.JoinSummary);

        var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);

        switcher.Run(args, config);
    }
}