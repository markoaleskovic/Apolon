namespace ORM.Core.Mapping.Attributes;

// serial identity
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public class DatabaseGeneratedIdentityAttribute : Attribute { }