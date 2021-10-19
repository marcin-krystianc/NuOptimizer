using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DependencyGraphFlattener;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using QuikGraph;

namespace NuOptimizer
{
    public class DependencyGraphFlattener
    {
        const string NuOptimizerLabel = "nuoptimizer";
        const string PackageReference = "PackageReference";
        const string ProjectReference = "ProjectReference";
        const string IncludeAssets = "IncludeAssets";
        const string PrivateAssets = "PrivateAssets";
        const string NuOptimizerSubDir = ".nuoptimizer";

        public void Apply(string rootPath)
        {
            var projectScanner = new ProjectScanner();
            var projectPaths = projectScanner.EnumerateProjects(rootPath).ToList();

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
                    rootPropsElement.Save(directoryBuildTargetsPath);
                }
            }

            // .nuoptimizer/.nuoptimizer.props
            using (var projectCollection = new ProjectCollection())
            {
                {
                    var nuOptimizerFile = Path.Combine(rootPath, NuOptimizerSubDir, ".nuoptimizer.props");
                    Directory.CreateDirectory(Path.GetDirectoryName(nuOptimizerFile));
                    File.WriteAllText(nuOptimizerFile, "<Project></Project>");
                    var nuOptimizerElement = ProjectRootElement.Open(nuOptimizerFile, projectCollection);
                    var import = nuOptimizerElement.AddImport(@"$(MSBuildProjectName).props");
                    import.Condition = @" '$(ManagePackageVersionsCentrally)' == 'true'" +
                                       @" and Exists('$(MSBuildProjectName).props') ";
                    nuOptimizerElement.Save();
                }
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

                var projects = projectPaths
                    .Select(x => Project.FromFile(x, projectOptions))
                    .ToList();

                var duplicate = projects
                    .GroupBy(x => x.GetProperty("MSBuildProjectName").EvaluatedValue)
                    .FirstOrDefault(x => Enumerable.Count<Project>(x) >= 2);

                if (duplicate != null)
                    throw new ApplicationException($"Unexpected duplicates in project file names {duplicate.Key}:" +
                                                   duplicate.Select(x => $"{Environment.NewLine}'{x.FullPath}'"));

                var graph = BuildProjectGraph(projects.Select(x => x.FullPath));
                var projectsDictionary = projects.ToDictionary(x => x.FullPath, x => x);

                foreach (var project in projects)
                {
                    var isManagedCentrally = project.GetProperty("ManagePackageVersionsCentrally");
                    if (isManagedCentrally?.EvaluatedValue != "true")
                        continue;

                    var projectName = project.GetProperty("MSBuildProjectName").EvaluatedValue;
                    var projectPropsPath = Path.Combine(rootPath, NuOptimizerSubDir, $"{projectName}.props");
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
                        .SelectMany(x => projectsDictionary[x].GetItems("PackageReference"))
                        .Select(x => x.EvaluatedInclude)
                        .Distinct()
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

                    projectPropsElement.Save();
                }
            }
        }

        private BidirectionalGraph<string, Edge<string>> BuildProjectGraph(IEnumerable<string> projectPaths)
        {
            using var projectCollection = new ProjectCollection();
            var projectOptions = new ProjectOptions
            {
                ProjectCollection = projectCollection,
                LoadSettings = ProjectLoadSettings.IgnoreEmptyImports | ProjectLoadSettings.IgnoreInvalidImports |
                               ProjectLoadSettings.RecordDuplicateButNotCircularImports |
                               ProjectLoadSettings.IgnoreMissingImports,
                EvaluationContext = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared),
            };

            var processedProjects = new HashSet<string>();
            var graph = new BidirectionalGraph<string, Edge<string>>();
            var queue = new Queue<string>(projectPaths.Select(Path.GetFullPath));

            while (queue.Count > 0)
            {
                var projectPath = queue.Dequeue();
                if (!processedProjects.Add(projectPath))
                    continue;

                var project = Project.FromFile(projectPath, projectOptions);
                var referencedProjects = project.GetItems("ProjectReference")
                    .Select(x => x.EvaluatedInclude)
                    .Select(x => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FullPath), x)))
                    .ToList();

                foreach (var referencedProject in referencedProjects)
                {
                    graph.AddVerticesAndEdge(new Edge<string>(projectPath, referencedProject));
                    queue.Enqueue(referencedProject);
                }
            }

            return graph;
        }

        private IEnumerable<string> EnumerateOutVerticesTransitively(BidirectionalGraph<string, Edge<string>> graph, string vertex)
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
