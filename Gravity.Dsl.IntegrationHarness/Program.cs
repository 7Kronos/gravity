using System;
using System.Collections.Generic;
using Gravity.Dsl.IntegrationHarness;
using Gravity.Dsl.IntegrationHarness.Subcommands;

// Disable MSBuild server auto-start to prevent port/lock contention when
// multiple dotnet build/pack invocations run from the same workspace.
Environment.SetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "0");
Environment.SetEnvironmentVariable("DOTNET_BUILD_SERVER_AUTOSTART", "0");

var opts = HarnessOptions.Parse(args);
var repoRoot = RepoLocator.FindRepoRoot();

SdkVersionCheck.WarnIfDrift("9.0.314");

var subcommands = new List<ISubcommand>
{
    new PackDeterminismSubcommand(),
    new ItemMetadataOverrideSubcommand(),
    new HookOrderSubcommand(),
    new EmptyInputSubcommand(),
    new NoGlobalToolSubcommand(),
    new IncrementalBuildSubcommand(),
};

var runner = new HarnessRunner(opts, repoRoot);

return opts.Subcommand switch
{
    "run-all" => runner.RunAll(subcommands),
    var name => runner.RunOne(
        subcommands.Find(s => string.Equals(s.SubcommandName, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException("Unknown subcommand: " + name)),
};
