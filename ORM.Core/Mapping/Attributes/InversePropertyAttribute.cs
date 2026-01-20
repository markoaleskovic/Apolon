namespace ORM.Core.Mapping.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class InversePropertyAttribute : Attribute
{
    public string InverseNavigationPropertyName { get; }

    public InversePropertyAttribute(string inverseNavigationPropertyName)
    {
        InverseNavigationPropertyName = inverseNavigationPropertyName;
    }
}