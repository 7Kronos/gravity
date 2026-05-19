using System;
using Gravity.Dsl.NupkgNormaliser;

// FR-3022: CLI surface — --input, --output (both required for normalise operation),
// --version (prints 1.0.0 and exits 0). Future flags are additive only.
string? input = null;
string? output = null;
bool versionFlag = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--input":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("error: --input requires a path argument");
                return 2;
            }
            input = args[++i];
            break;

        case "--output":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("error: --output requires a path argument");
                return 2;
            }
            output = args[++i];
            break;

        case "--version":
            versionFlag = true;
            break;

        default:
            Console.Error.WriteLine($"error: unrecognised flag '{args[i]}'");
            Console.Error.WriteLine("usage: nupkg-normaliser --input <path> --output <path>");
            Console.Error.WriteLine("       nupkg-normaliser --version");
            return 2;
    }
}

if (versionFlag)
{
    Console.WriteLine("1.0.0");
    return 0;
}

if (input is null || output is null)
{
    Console.Error.WriteLine("error: --input and --output are both required");
    Console.Error.WriteLine("usage: nupkg-normaliser --input <path> --output <path>");
    return 2;
}

NupkgNormalizer.Normalize(input, output);
return 0;
