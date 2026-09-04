namespace OpenCamInterop.EventLab;

internal sealed class CliOptions
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    private CliOptions(IReadOnlyDictionary<string, string?> values)
    {
        _values = values;
    }

    internal bool Has(string name) => _values.ContainsKey(name);

    internal string Require(string name)
    {
        return _values.TryGetValue(name, out var value) && value is not null
            ? value
            : throw new EventLabInputException("cli.option-required", $"The --{name} option is required.");
    }

    internal string? Optional(string name)
    {
        return _values.TryGetValue(name, out var value) ? value : null;
    }

    internal static CliOptions Parse(
        IReadOnlyList<string> arguments,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> flagOptions)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
                throw new EventLabInputException("cli.argument", "Only named --options are accepted after the command.");

            var separator = argument.IndexOf('=', 2);
            var name = separator >= 0 ? argument[2..separator] : argument[2..];
            var inlineValue = separator >= 0 ? argument[(separator + 1)..] : null;
            if (!valueOptions.Contains(name) && !flagOptions.Contains(name))
                throw new EventLabInputException("cli.option-unknown", $"The --{name} option is not supported.");
            if (!values.TryAdd(name, null))
                throw new EventLabInputException("cli.option-duplicate", $"The --{name} option can be specified only once.");

            if (flagOptions.Contains(name))
            {
                if (inlineValue is not null)
                    throw new EventLabInputException("cli.option-value", $"The --{name} flag does not accept a value.");
                continue;
            }

            string value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else
            {
                if (++index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new EventLabInputException("cli.option-value", $"The --{name} option requires a value.");
                value = arguments[index];
            }

            if (value.Length == 0)
                throw new EventLabInputException("cli.option-value", $"The --{name} option cannot be empty.");
            values[name] = value;
        }

        return new CliOptions(values);
    }
}

internal sealed class EventLabInputException : Exception
{
    internal EventLabInputException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    internal string Code { get; }
}
