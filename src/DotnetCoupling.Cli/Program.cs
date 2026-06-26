using DotnetCoupling.Analysis;
using DotnetCoupling.Cli;

var options = ArgParser.Parse(args, out var error);
if (error is not null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(ArgParser.Usage);
    return 2; // 設定エラー
}
if (options is null)
{
    Console.WriteLine(ArgParser.Usage);
    return 0;
}

try
{
    // MSBuild の登録は、workspace を使うコードより前に必ず実行する。
    var desc = MsBuildRegistrar.EnsureRegistered();
    Console.Error.WriteLine($"MSBuild: {desc}");

    return await CliRunner.RunAsync(options).ConfigureAwait(false);
}
catch (Exception ex)
{
    Console.Error.WriteLine("analysis failed: " + ex.Message);
    for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        Console.Error.WriteLine("  inner: " + inner.Message);
    return 3; // 解析失敗
}
