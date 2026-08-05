using System.Reflection;
using System.Runtime.Loader;

namespace Server.Scripting
{
    public sealed class ScriptLoadContext : AssemblyLoadContext
    {
        public ScriptLoadContext() : base(isCollectible: true)
        {
            Resolving += OnResolving;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            return null;
        }

        private static Assembly OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (assemblyName == null) return null;
            if (string.IsNullOrWhiteSpace(assemblyName.Name)) return null;

            // 优先复用 Default ALC 中已加载的程序集，避免类型标识不一致。
            var loaded = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

            if (loaded != null) return loaded;

            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                return null;
            }
        }
    }
}
