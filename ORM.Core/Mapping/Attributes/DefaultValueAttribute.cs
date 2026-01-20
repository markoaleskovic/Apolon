namespace ORM.Core.Mapping.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DefaultValueAttribute : Attribute
{
    public object? Value { get; }

    public DefaultValueAttribute(object? value)
    {
        Value = value;
    }
}