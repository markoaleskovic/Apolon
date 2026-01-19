namespace ORM.Core.Mapping.Attributes;


[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}