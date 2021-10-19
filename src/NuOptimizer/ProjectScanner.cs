using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DependencyGraphFlattener
{
    internal class ProjectScanner
    {
        public IEnumerable<string> EnumerateProjects(string rootPath)
        {
            var extensions = new HashSet<string> { ".csproj", ".fsproj" };
            return Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f)));
        }
    }
}
