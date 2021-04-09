﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommandLine;

// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable SuggestVarOrType_Elsewhere

namespace OneLocBuildSetup
{
    [SuppressMessage("Style", "IDE0008:Use explicit type")]
    internal class Program
    {
        private const string XlfExtension = ".xlf";
        private const string SpanishXlfExtension = ".es" + XlfExtension;

        public static void Main(string[] args) => Parser.Default.ParseArguments<Arguments>(args).WithParsed(RunSetup);

        private static void RunSetup(Arguments args)
        {
            var srcPath = Path.Combine(args.RepositoryPath, "src");
            var esXlfPaths = Directory.GetFiles(srcPath, $"*{SpanishXlfExtension}", SearchOption.AllDirectories);
            var filePaths = esXlfPaths.Select(es => (esXlfPath: es, xlfPath: es.Replace(SpanishXlfExtension, XlfExtension))).ToArray();
            foreach ((string esXlfPath, string xlfPath) in filePaths)
            {
                Console.WriteLine($"Creating {xlfPath}");
                File.Copy(esXlfPath, xlfPath, true);
            }

            Console.WriteLine($"Path Separator: {Path.PathSeparator}");
            var locProject = new LocProject(filePaths
                .Select(fp => (
                    xlfPath: fp.xlfPath.Remove(0, args.RepositoryPath.Length).TrimStart('\\').TrimStart('/'),
                    projectName: GetProjectName(fp.xlfPath, srcPath)))
                .GroupBy(p => p.projectName)
                .OrderBy(pg => pg.Key)
                .Select(pg => new Project(pg
                    .Select(p => CreateLocItem(p.xlfPath))
                    .OrderBy(li => li.SourceFile)
                    .ToArray()))
                .ToArray());

            var locPath = Path.Combine(args.OutputPath, "loc");
            Directory.CreateDirectory(locPath);
            var locProjectPath = Path.Combine(locPath, "LocProject.json");
            var locProjectJson = JsonSerializer.Serialize(locProject, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"Creating {locProjectPath}");
            File.WriteAllText(locProjectPath, locProjectJson);
        }

        private static string GetProjectName(string xlfPath, string srcPath) => xlfPath
            .Remove(0, srcPath.Length)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        private static LocItem CreateLocItem(string xlfPath)
        {
            var outputPath = Path.GetDirectoryName(xlfPath) ?? string.Empty;
            var lclPath = Path.Combine(outputPath, "{Lang}", Path.GetFileName(xlfPath).Replace(XlfExtension, $"{XlfExtension}.lcl"));
            return new LocItem(xlfPath, outputPath, lclPath);
        }
    }
}
