﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio.LanguageServices.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices
{
    /// <summary>
    ///     Creates and handles releasing a collection of <see cref="IWorkspaceProjectContext"/> instances for a given cross targeting project.
    /// </summary>
    internal sealed class AggregateWorkspaceProjectContext
    {
        private readonly ImmutableDictionary<string, IWorkspaceProjectContext> _configuredProjectContextsByTargetFramework;
        private readonly ImmutableDictionary<string, ConfiguredProject> _configuredProjectsByTargetFramework;
        private readonly string _activeTargetFramework;
        private readonly IUnconfiguredProjectHostObject _unconfiguredProjectHostObject;
        private HashSet<IWorkspaceProjectContext> _disposedConfiguredProjectContexts;

        public AggregateWorkspaceProjectContext(
            ImmutableDictionary<string, IWorkspaceProjectContext> configuredProjectContextsByTargetFramework,
            ImmutableDictionary<string, ConfiguredProject> configuredProjectsByTargetFramework,
            string activeTargetFramework,
            IUnconfiguredProjectHostObject unconfiguredProjectHostObject)
        {
            Requires.NotNullOrEmpty(configuredProjectContextsByTargetFramework, nameof(configuredProjectContextsByTargetFramework));
            Requires.NotNullOrEmpty(configuredProjectsByTargetFramework, nameof(configuredProjectsByTargetFramework));
            Requires.NotNull(activeTargetFramework, nameof(activeTargetFramework));
            Requires.Argument(configuredProjectContextsByTargetFramework.ContainsKey(activeTargetFramework), nameof(configuredProjectContextsByTargetFramework), "Missing IWorkspaceProjectContext for activeTargetFramework");
            Requires.NotNull(unconfiguredProjectHostObject, nameof(unconfiguredProjectHostObject));

            _configuredProjectContextsByTargetFramework = configuredProjectContextsByTargetFramework;
            _configuredProjectsByTargetFramework = configuredProjectsByTargetFramework;
            _activeTargetFramework = activeTargetFramework;
            _unconfiguredProjectHostObject = unconfiguredProjectHostObject;
            _disposedConfiguredProjectContexts = new HashSet<IWorkspaceProjectContext>();
        }

        // IWorkspaceProjectContext implements the VS-only interface IVsLanguageServiceBuildErrorReporter2
        public object HostSpecificErrorReporter => InnerProjectContexts.First();

        public IEnumerable<IWorkspaceProjectContext> InnerProjectContexts => _configuredProjectContextsByTargetFramework.Values;

        public ImmutableArray<IWorkspaceProjectContext> DisposedInnerProjectContexts => _disposedConfiguredProjectContexts.ToImmutableArray();

        public IEnumerable<ConfiguredProject> InnerConfiguredProjects => _configuredProjectsByTargetFramework.Values;

        public IWorkspaceProjectContext ActiveProjectContext => _configuredProjectContextsByTargetFramework[_activeTargetFramework];

        public bool IsCrossTargeting => _activeTargetFramework.Length > 0;
        
        public void SetProjectFilePathAndDisplayName(string projectFilePath, string displayName)
        {
            // Update the project file path and display name for all the inner project contexts.
            foreach (var innerProjectContextKvp in _configuredProjectContextsByTargetFramework)
            {
                var targetFramework = innerProjectContextKvp.Key;
                var innerProjectContext = innerProjectContextKvp.Value;
                
                // For cross targeting projects, we ensure that the display name is unique per every target framework.
                innerProjectContext.DisplayName = IsCrossTargeting ? $"{displayName}({targetFramework})" : displayName;
                innerProjectContext.ProjectFilePath = projectFilePath;
            }
        }

        public IWorkspaceProjectContext GetInnerProjectContext(ProjectConfiguration projectConfiguration, out bool isActiveConfiguration)
        {
            if (projectConfiguration.IsCrossTargeting())
            {
                var targetFramework = projectConfiguration.Dimensions[TargetFrameworkProjectConfigurationDimensionProvider.TargetFrameworkPropertyName];
                isActiveConfiguration = string.Equals(targetFramework, _activeTargetFramework);

                IWorkspaceProjectContext projectContext;
                return _configuredProjectContextsByTargetFramework.TryGetValue(targetFramework, out projectContext) ?
                    projectContext :
                    null;
            }
            else
            {
                isActiveConfiguration = true;
                if (_configuredProjectContextsByTargetFramework.Count > 1)
                {
                    return null;
                }

                return InnerProjectContexts.Single();
            }
        }

        /// <summary>
        /// Returns true if this cross-targeting aggregate project context has the same set of target frameworks and active target framework as the given TargetFrameworks property value.
        /// </summary>
        /// <param name="targetFrameworksProperty">Property value for MSBuild property "TargetsFramework"</param>
        public bool HasMatchingTargetFrameworks(string targetFrameworksProperty)
        {
            Requires.NotNull(targetFrameworksProperty, nameof(targetFrameworksProperty));
            Requires.Range(IsCrossTargeting, nameof(targetFrameworksProperty), "This method should only be invoked for Cross targeting projects");

            ImmutableArray<string> parsedTargetFrameworks = TargetFrameworkProjectConfigurationDimensionProvider.ParseTargetFrameworks(targetFrameworksProperty);
            if (parsedTargetFrameworks.Length != _configuredProjectContextsByTargetFramework.Count)
            {
                // Different number of target frameworks.
                return false;
            }

            if (!string.Equals(parsedTargetFrameworks[0], _activeTargetFramework))
            {
                // Active target framework is different.
                return false;
            }

            foreach (var targetFramework in parsedTargetFrameworks.Skip(1))
            {
                if (!_configuredProjectContextsByTargetFramework.ContainsKey(targetFramework))
                {
                    // Differing TargetFramework
                    return false;
                }
            }

            return true;
        }

        public void Dispose(Func<IWorkspaceProjectContext, bool> shouldDisposeInnerContext)
        {
            // Dispose the host object.
            _unconfiguredProjectHostObject.Dispose();

            // Dispose the inner project contexts.
            var disposedContexts = new List<IWorkspaceProjectContext>();
            foreach (var innerProjectContext in InnerProjectContexts)
            {
                if (shouldDisposeInnerContext(innerProjectContext))
                {
                    innerProjectContext.Dispose();
                    disposedContexts.Add(innerProjectContext);
                }
            }

            lock (_disposedConfiguredProjectContexts)
            {
                _disposedConfiguredProjectContexts.AddRange(disposedContexts);
            }
        }
    }
}
