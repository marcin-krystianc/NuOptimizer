using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DependencyGraphFlattener;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using QuikGraph;
using Serilog;

namespace NuOptimizer
{
    public class DependencyGraphFlattener
    {
        const string NuOptimizerLabel = "nuoptimizer";
        const string PackageReference = "PackageReference";
        const string ProjectReference = "ProjectReference";
        const string PrivateAssets = "PrivateAssets";
        const string NuOptimizerSubDir = ".nuoptimizer";

        public void Apply(string rootPath)
        {
            var projectScanner = new ProjectScanner();
            var projectPaths = projectScanner.EnumerateProjects(rootPath).ToList();

            Log.Information($"Working directory: '{rootPath}'.");

            var subDir = Path.Combine(rootPath, NuOptimizerSubDir);
            if (Directory.Exists(subDir))
            {
                Log.Information($"Cleaning files in '{NuOptimizerSubDir}'.");
                Directory.Delete(subDir, recursive: true);
            }

            // Directory.Build.targets
            using (var projectCollection = new ProjectCollection())
            {
                var directoryBuildTargetsPath = Path.Combine(rootPath, "Directory.Build.targets");
                {
                    ProjectRootElement rootPropsElement;
                    if (File.Exists(directoryBuildTargetsPath))
                    {
                        rootPropsElement = ProjectRootElement.Open(directoryBuildTargetsPath, projectCollection);
                        var importToRemove = rootPropsElement.Imports.FirstOrDefault(x => x.Label == NuOptimizerLabel);
                        if (importToRemove != null)
                        {
                            rootPropsElement.RemoveChild(importToRemove);
                        }
                    }
                    else
                    {
                        File.WriteAllText(directoryBuildTargetsPath, "<Project></Project>");
                        rootPropsElement = ProjectRootElement.Open(directoryBuildTargetsPath, projectCollection);
                        var targetsImport = rootPropsElement.AddImport(
                            @"$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory)../, Directory.Build.targets))\Directory.Build.targets");
                        targetsImport.Condition =
                            " '$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildThisFileDirectory)../, Directory.Build.targets))' != '' ";
                    }

                    var import = rootPropsElement.AddImport(@".nuoptimizer\.nuoptimizer.props");
                    import.Condition = @"Exists('.nuoptimizer\.nuoptimizer.props')";
                    import.Label = NuOptimizerLabel;

                    Log.Information($"Writing '{Path.GetRelativePath(rootPath, rootPropsElement.FullPath)}'.");
                    rootPropsElement.Save(directoryBuildTargetsPath);
                }
            }

            // .nuoptimizer/.nuoptimizer.props
            using (var projectCollection = new ProjectCollection())
            {
                var nuOptimizerFile = Path.Combine(subDir, ".nuoptimizer.props");
                Directory.CreateDirectory(Path.GetDirectoryName(nuOptimizerFile));
                File.WriteAllText(nuOptimizerFile, "<Project></Project>");
                var nuOptimizerElement = ProjectRootElement.Open(nuOptimizerFile, projectCollection);
                var import = nuOptimizerElement.AddImport(@"$(MSBuildProjectName).props");
                import.Condition = @" '$(ManagePackageVersionsCentrally)' == 'true'" +
                                   @" and Exists('$(MSBuildProjectName).props') ";

                Log.Information($"Writing '{Path.GetRelativePath(rootPath, nuOptimizerElement.FullPath)}'.");
                nuOptimizerElement.Save();
            }

            using (var projectCollection = new ProjectCollection())
            {
                var projectOptions = new ProjectOptions
                {
                    ProjectCollection = projectCollection,
                    LoadSettings = ProjectLoadSettings.IgnoreEmptyImports | ProjectLoadSettings.IgnoreInvalidImports |
                                   ProjectLoadSettings.RecordDuplicateButNotCircularImports |
                                   ProjectLoadSettings.IgnoreMissingImports,
                    EvaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared),
                };

                var projects = new ConcurrentDictionary<string, Project>(StringComparer.OrdinalIgnoreCase);

                foreach (var projectPath in projectPaths)
                {
                    projects.TryAdd(projectPath, Project.FromFile(projectPath, projectOptions));
                }

                var duplicate = projects.Values
                    .GroupBy(x => x.GetProperty("MSBuildProjectName").EvaluatedValue)
                    .FirstOrDefault(x => Enumerable.Count<Project>(x) >= 2);

                if (duplicate != null)
                    throw new ApplicationException($"Unexpected duplicates in project file names {duplicate.Key}:" +
                                                   duplicate.Select(x => $"{Environment.NewLine}'{x.FullPath}'"));

                var graph = BuildProjectGraph(projects.Keys, x => projects.GetOrAdd(x, x => Project.FromFile(x, projectOptions)));

                var propsCounter = 0;
                foreach (var project in projectPaths.Select(x => projects[x]))
                {
                    var isManagedCentrally = project.GetProperty("ManagePackageVersionsCentrally");
                    if (isManagedCentrally?.EvaluatedValue != "true")
                        continue;

                    var projectName = project.GetProperty("MSBuildProjectName").EvaluatedValue;
                    var projectPropsPath = Path.Combine(subDir, $"{projectName}.props");
                    File.WriteAllText(projectPropsPath, "<Project></Project>");
                    var projectPropsElement = ProjectRootElement.Open(projectPropsPath, new ProjectCollection());

                    var transitiveProjects = EnumerateOutVerticesTransitively(graph, project.FullPath)
                        .ToList();

                    var centralPackages = project.GetItems("PackageVersion")
                        .Select(x => x.EvaluatedInclude)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    var transitivePackages = transitiveProjects
                        .SelectMany(x => projects[x].GetItems("PackageReference"))
                        .Select(x => x.EvaluatedInclude)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

                    var packages = transitivePackages.Intersect(centralPackages);

                    foreach (var package in packages)
                    {
                        var item = projectPropsElement.AddItem(PackageReference, package);
                        /*
                        var includeAssetsMetadata = item.AddMetadata(IncludeAssets, "none");
                        includeAssetsMetadata.ExpressedAsAttribute = true;
                        */
                        var privateAssetsMetadata = item.AddMetadata(PrivateAssets, "all");
                        privateAssetsMetadata.ExpressedAsAttribute = true;
                    }

                    foreach (var projectPath in transitiveProjects)
                    {
                        var relativePath = Path.GetRelativePath(Path.GetDirectoryName(project.FullPath), projectPath)
                            // convert to windows-style separators
                            .Replace("/", "\\");

                        var item = projectPropsElement.AddItem(ProjectReference, relativePath);
                        /*
                        var includeAssetsMetadata = item.AddMetadata(IncludeAssets, "none");
                        includeAssetsMetadata.ExpressedAsAttribute = true;
                        */
                        var privateAssetsMetadata = item.AddMetadata(PrivateAssets, "all");
                        privateAssetsMetadata.ExpressedAsAttribute = true;
                    }

                    propsCounter++;
                    projectPropsElement.Save();
                }

                Log.Information($"Written {propsCounter} '{Path.Combine(NuOptimizerSubDir, "*.props")}' files.");
            }
        }

        private BidirectionalGraph<string, Edge<string>> BuildProjectGraph(
            IEnumerable<string> paths, Func<string, Project> projectLoader)
        {
            var processedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var graph = new BidirectionalGraph<string, Edge<string>>();
            var queue = new Queue<string>(paths);

            while (queue.Count > 0)
            {
                var projectPath = queue.Dequeue();
                var project = projectLoader(projectPath);
                if (!processedProjects.Add(project.FullPath))
                    continue;

                graph.AddVertex(project.FullPath);

                var referencedPaths = project.GetItems("ProjectReference")
                    .Select(x => x.EvaluatedInclude)
                    .Select(x => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FullPath), x)))
                    .ToList();

                foreach (var referencedPath in referencedPaths)
                {
                    if (!File.Exists(referencedPath))
                    {
                        // TODO - implement strict mode?
                        if (missingProjects.Add(referencedPath))
                        {
                            Log.Warning($"File '{referencedPath}' doesn't exist!");
                        }

                        continue;
                    }

                    var referencedProject = projectLoader(referencedPath);
                    graph.AddVerticesAndEdge(new Edge<string>(project.FullPath, referencedProject.FullPath));
                    queue.Enqueue(referencedPath);
                }
            }

            return graph;
        }

        private IEnumerable<string> EnumerateOutVerticesTransitively(BidirectionalGraph<string, Edge<string>> graph,
            string vertex)
        {
            var visitedProjects = new HashSet<string>();
            var queue = new Queue<string>(new[] { vertex });
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var outVertices = graph.OutEdges(current);
                foreach (var outVertex in outVertices)
                {
                    if (!visitedProjects.Add(outVertex.Target))
                        continue;

                    yield return outVertex.Target;
                    queue.Enqueue(outVertex.Target);
                }
            }
        }
    }
}
