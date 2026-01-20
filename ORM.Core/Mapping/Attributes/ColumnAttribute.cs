namespace ORM.Core.Mapping.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}