// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using MonoDevelop.Projects;

namespace Microsoft.VisualStudio.Mac.LanguageServices.Razor.ProjectSystem
{
    internal class DefaultRazorProjectHost : RazorProjectHostBase
    {
        private const string RazorLangVersionProperty = "RazorLangVersion";
        private const string RazorDefaultConfigurationProperty = "RazorDefaultConfiguration";
        private const string RazorExtensionItemType = "RazorExtension";
        private const string RazorConfigurationItemType = "RazorConfiguration";
        private const string RazorConfigurationItemTypeExtensionsProperty = "Extensions";

        public DefaultRazorProjectHost(
            DotNetProject project,
            ForegroundDispatcher foregroundDispatcher,
            ProjectSnapshotManagerBase projectSnapshotManager)
            : base(project, foregroundDispatcher, projectSnapshotManager)
        {
        }

        protected override async Task OnProjectChangedAsync()
        {
            ForegroundDispatcher.AssertBackgroundThread();

            await ExecuteWithLockAsync(async () =>
            {
                var projectProperties = DotNetProject.MSBuildProject.EvaluatedProperties;
                var languageVersion = projectProperties.GetProperty(RazorLangVersionProperty)?.Value;
                var defaultConfiguration = projectProperties.GetProperty(RazorDefaultConfigurationProperty)?.Value;

                RazorConfiguration configuration = null;
                if (!string.IsNullOrEmpty(languageVersion) && !string.IsNullOrEmpty(defaultConfiguration))
                {
                    if (!RazorLanguageVersion.TryParse(languageVersion, out var parsedVersion))
                    {
                        parsedVersion = RazorLanguageVersion.Latest;
                    }

                    var projectItems = DotNetProject.MSBuildProject.EvaluatedItems;
                    configuration = projectItems
                        .Where(item => item.Name == RazorConfigurationItemType && item.Include == defaultConfiguration)
                        .Select(config =>
                        {
                            var extensions = projectItems
                                .Where(item => item.Name == RazorExtensionItemType)
                                .Select(extension => new ProjectSystemRazorExtension(extension.Include));
                            var configurationExtensions = config.Metadata.GetProperty(RazorConfigurationItemTypeExtensionsProperty).Value.Split(';');
                            var includedExtensions = extensions.Where(extension => configurationExtensions.Contains(extension.ExtensionName)).ToArray();

                            return new ProjectSystemRazorConfiguration(parsedVersion, config.Include, includedExtensions);
                        })
                        .FirstOrDefault();
                }

                if (configuration == null)
                {
                    // Ok we can't find a language version. Let's assume this project isn't using Razor then.
                    await UpdateHostProjectUnsafeAsync(null).ConfigureAwait(false);
                    return;
                }

                var hostProject = new HostProject(DotNetProject.FileName.FullPath, configuration);
                await UpdateHostProjectUnsafeAsync(hostProject).ConfigureAwait(false);
            });
        }
    }
}
