using System;
using System.Collections.Generic;
using System.Linq;

namespace QueryGuard.Cli;

/// <summary>
/// The parsed command line.
/// </summary>
/// <remarks>
/// Hand-written rather than pulled from a parsing library. This tool has two commands and five options;
/// a dependency would be larger than the code it replaced, and a tool whose argument is that it does not
/// change how your build behaves is a poor place to add one. `System.CommandLine` is also still moving,
/// and a shipped tool pinned to a moving preview is a maintenance bill.
/// </remarks>
internal sealed class CommandLine
{
    private CommandLine(string command, IReadOnlyDictionary<string, string> options, IReadOnlyList<string> flags)
    {
        Command = command;
        Options = options;
        Flags = flags;
    }

    internal string Command { get; }

    internal IReadOnlyDictionary<string, string> Options { get; }

    internal IReadOnlyList<string> Flags { get; }

    /// <summary>
    /// Parses arguments, or explains why they cannot be parsed.
    /// </summary>
    /// <param name="args">The raw arguments.</param>
    /// <param name="parsed">The parsed command line, when parsing succeeded.</param>
    /// <param name="error">What is wrong, when it did not.</param>
    /// <returns>Whether parsing succeeded.</returns>
    internal static bool TryParse(string[] args, out CommandLine? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (args.Length == 0)
        {
            error = "No command given.";
            return false;
        }

        // "baseline record" reads better than "baseline-record" and is one token to the user, so the
        // two words are joined here rather than modelled as a command with a subcommand.
        var index = 0;
        var command = args[0];

        if (string.Equals(command, "baseline", StringComparison.Ordinal))
        {
            if (args.Length < 2 || args[1].StartsWith('-'))
            {
                error = "The 'baseline' command needs a subcommand. The only one is 'record'.";
                return false;
            }

            command = $"baseline {args[1]}";
            index = 2;
        }
        else
        {
            index = 1;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new List<string>();

        while (index < args.Length)
        {
            var token = args[index];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{token}'.";
                return false;
            }

            var name = token[2..];

            if (name.Length == 0)
            {
                error = "An option name is missing after '--'.";
                return false;
            }

            // A value only if the next token is not itself an option. That makes flags and options
            // distinguishable without a schema, at the cost of rejecting values that begin with "--",
            // which no path or title needs.
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = args[index + 1];
                index += 2;
                continue;
            }

            flags.Add(name);
            index++;
        }

        parsed = new CommandLine(command, options, flags);
        return true;
    }

    internal string Option(string name, string fallback)
        => Options.TryGetValue(name, out var value) ? value : fallback;

    internal string? OptionOrNull(string name)
        => Options.TryGetValue(name, out var value) ? value : null;

    internal bool HasFlag(string name) => Flags.Contains(name);

    /// <summary>
    /// Names any option or flag this command does not understand.
    /// </summary>
    /// <remarks>
    /// A typo in an option name would otherwise be silently ignored, and the run would look like it
    /// worked while doing something else. A misspelt <c>--baseline</c> writing to the default path is
    /// exactly the kind of quiet wrong answer this tool exists to avoid producing.
    /// </remarks>
    internal string? FindUnknown(params string[] known)
        => Options.Keys
            .Concat(Flags)
            .FirstOrDefault(name => Array.IndexOf(known, name) < 0);
}
