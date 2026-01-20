
namespace ORM.Core.Mapping.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ForeignKeyAttribute : Attribute
{
    public string ForeignKeyPropertyName { get; }

    public ForeignKeyAttribute(string foreignKeyPropertyName)
    {
        ForeignKeyPropertyName = foreignKeyPropertyName;
    }
}