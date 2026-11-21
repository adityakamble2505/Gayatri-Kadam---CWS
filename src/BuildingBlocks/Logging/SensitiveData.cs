using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace GreenCarWash.BuildingBlocks.Logging;

public static class SensitiveData
{
    public static bool IsSensitive(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Contains("password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cvv", StringComparison.OrdinalIgnoreCase)
            || name.Contains("card", StringComparison.OrdinalIgnoreCase)
            || name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pan", StringComparison.OrdinalIgnoreCase)
            || name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
            || name.Equals("internalkey", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CorrelationIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = Correlation.CorrelationContext.Id;
        if (string.IsNullOrWhiteSpace(correlationId))
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
    }
}

public sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToArray())
        {
            if (SensitiveData.IsSensitive(property.Key))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue("***")));
                continue;
            }

            var redacted = Redact(property.Value);
            if (!ReferenceEquals(redacted, property.Value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, redacted));
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case StructureValue structure:
                var structureChanged = false;
                var properties = new List<LogEventProperty>(structure.Properties.Count);
                foreach (var child in structure.Properties)
                {
                    if (SensitiveData.IsSensitive(child.Name))
                    {
                        structureChanged = true;
                        properties.Add(new LogEventProperty(child.Name, new ScalarValue("***")));
                        continue;
                    }

                    var redactedChild = Redact(child.Value);
                    if (!ReferenceEquals(redactedChild, child.Value))
                        structureChanged = true;
                    properties.Add(new LogEventProperty(child.Name, redactedChild));
                }

                return structureChanged ? new StructureValue(properties, structure.TypeTag) : value;

            case SequenceValue sequence:
                var sequenceChanged = false;
                var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);
                foreach (var element in sequence.Elements)
                {
                    var redactedElement = Redact(element);
                    if (!ReferenceEquals(redactedElement, element))
                        sequenceChanged = true;
                    elements.Add(redactedElement);
                }

                return sequenceChanged ? new SequenceValue(elements) : value;

            case DictionaryValue dictionary:
                var dictionaryChanged = false;
                var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
                foreach (var entry in dictionary.Elements)
                {
                    var key = entry.Key.Value?.ToString();
                    if (SensitiveData.IsSensitive(key))
                    {
                        dictionaryChanged = true;
                        entries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(entry.Key, new ScalarValue("***")));
                        continue;
                    }

                    var redactedEntry = Redact(entry.Value);
                    if (!ReferenceEquals(redactedEntry, entry.Value))
                        dictionaryChanged = true;
                    entries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(entry.Key, redactedEntry));
                }

                return dictionaryChanged ? new DictionaryValue(entries) : value;

            case ScalarValue scalar when scalar.Value is string text:
                var scrubbed = ScrubCardNumbers(text);
                return scrubbed == text ? value : new ScalarValue(scrubbed);

            default:
                return value;
        }
    }

    internal static string ScrubCardNumbers(string text)
    {
        var chars = text.ToCharArray();
        var changed = false;
        var index = 0;
        while (index < chars.Length)
        {
            if (!char.IsDigit(chars[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < chars.Length && char.IsDigit(chars[index]))
                index++;

            var length = index - start;
            if (length is < 13 or > 19)
                continue;

            changed = true;
            for (var digit = start; digit < index; digit++)
                chars[digit] = '*';
        }

        return changed ? new string(chars) : text;
    }
}

public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        result = null!;
        if (value is Exception or string or ValueType)
            return false;

        var type = value.GetType();
        var typeNamespace = type.Namespace;
        if (typeNamespace is not null && !typeNamespace.StartsWith("GreenCarWash", StringComparison.Ordinal))
            return false;

        var properties = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();

        if (properties.Length == 0)
            return false;

        var logProperties = new List<LogEventProperty>(properties.Length);
        foreach (var property in properties)
        {
            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            if (SensitiveData.IsSensitive(property.Name))
            {
                logProperties.Add(new LogEventProperty(property.Name, new ScalarValue("***")));
                continue;
            }

            logProperties.Add(new LogEventProperty(
                property.Name,
                propertyValueFactory.CreatePropertyValue(propertyValue, destructureObjects: true)));
        }

        result = new StructureValue(logProperties, type.Name);
        return true;
    }
}
