using System.Reflection;
using ORM.Core.Mapping.Attributes;

namespace ORM.Migrations.Core;

public static class EntityDiscovery
{
    public static Type[] DiscoverEntities(string assemblyPath, string? @namespace = null)
    {
        var asm = Assembly.LoadFrom(assemblyPath);

        var types = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic)
            .Where(t => t.GetCustomAttribute<TableAttribute>(inherit: false) is not null)
            .Where(t => @namespace is null || (t.Namespace?.StartsWith(@namespace) ?? false))
            .ToArray();

        if (types.Length == 0)
            throw new InvalidOperationException($"No [Table] entities found in {assemblyPath} (namespace filter: '{@namespace}').");

        return types;
    }
}