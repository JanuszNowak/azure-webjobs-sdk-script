﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.ExtensionRequirements;
using Microsoft.Extensions.DependencyModel;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public static class DependencyHelper
    {
        private const string AssemblyNamePrefix = "assembly:";
        private static readonly Lazy<Dictionary<string, string[]>> _ridGraph = new Lazy<Dictionary<string, string[]>>(BuildRuntimesGraph);
        private static string _runtimeIdentifier;

        private static Dictionary<string, string[]> BuildRuntimesGraph()
        {
            var ridGraph = new Dictionary<string, string[]>();
            string runtimesJson = GetRuntimesGraphJson();
            var runtimes = (JObject)JObject.Parse(runtimesJson)["runtimes"];

            foreach (var runtime in runtimes)
            {
                string[] imports = ((JObject)runtime.Value)["#import"]
                    ?.Values<string>()
                    .ToArray();

                ridGraph.Add(runtime.Key, imports);
            }

            return ridGraph;
        }

        private static string GetDefaultPlatformRid()
        {
            // This logic follows what the .NET Core host does in: https://github.com/dotnet/core-setup/blob/master/src/corehost/common/pal.h

            // When running on a platform that is not supported in RID fallback graph (because it was unknown
            // at the time the SharedFX in question was built), we need to use a reasonable fallback RID to allow
            // consuming the native assets.
            //
            // For Windows and OSX, we will maintain the last highest RID-Platform we are known to support for them as the
            // degree of compat across their respective releases is usually high.
            //
            // We cannot maintain the same (compat) invariant for linux and thus, we will fallback to using lowest RID-Plaform.

            string rid = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                rid = DotNetConstants.DefaultWindowsRID;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                rid = DotNetConstants.DefaultOSXRID;
            }
            else
            {
                rid = DotNetConstants.DefaultLinuxRID;
            }

            return rid;
        }

        private static string GetRuntimesGraphJson()
        {
            return GetResourceFileContents("runtimes.json");
        }

        private static string GetResourceFileContents(string fileName)
        {
            var assembly = typeof(DependencyHelper).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.{fileName}"))
            using (var reader = new StreamReader(resource))
            {
                return reader.ReadToEnd();
            }
        }

        internal static Dictionary<string, ScriptRuntimeAssembly> GetRuntimeAssemblies(string assemblyManifestName)
        {
            string assembliesJson = GetResourceFileContents(assemblyManifestName);
            JObject assemblies = JObject.Parse(assembliesJson);

            return assemblies["runtimeAssemblies"]
                .ToObject<ScriptRuntimeAssembly[]>()
                .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        }

        internal static ExtensionRequirementsInfo GetExtensionRequirements()
        {
            string requirementsJson = GetResourceFileContents("extensionrequirements.json");
            JObject requirements = JObject.Parse(requirementsJson);

            var bundleRequirements = requirements["bundles"]
                .ToObject<BundleRequirement[]>();

            var extensionRequirements = requirements["types"]
                .ToObject<ExtensionStartupTypeRequirement[]>();

            return new ExtensionRequirementsInfo(bundleRequirements, extensionRequirements);
        }

        /// <summary>
        /// Gets the default runtime fallback RIDs for a given RID.
        /// The graph used to build the fallback list is static and
        /// useful in self-contained scenarios, where this information
        /// is not available at runtime
        /// </summary>
        /// <param name="rid">The runtime identifier to lookup.</param>
        /// <returns>The runtime fallbacks for the provided identifier.</returns>
        public static RuntimeFallbacks GetDefaultRuntimeFallbacks(string rid)
        {
            var ridGraph = _ridGraph.Value;

            var runtimeFallbacks = new RuntimeFallbacks(rid);
            var fallbacks = new List<string>();

            if (!ridGraph.ContainsKey(rid))
            {
                rid = GetDefaultPlatformRid();
                fallbacks.Add(rid);
            }

            var queue = new Queue<string>(ridGraph[rid]);

            while (queue.Count > 0)
            {
                var currentRid = queue.Dequeue();

                if (fallbacks.Contains(currentRid))
                {
                    continue;
                }

                fallbacks.Add(currentRid);

                foreach (var fallbackRid in ridGraph[currentRid])
                {
                    if (!fallbacks.Contains(fallbackRid, StringComparer.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(fallbackRid);
                    }
                }
            }

            runtimeFallbacks.Fallbacks = fallbacks.AsReadOnly();
            return runtimeFallbacks;
        }

        public static List<string> GetRuntimeFallbacks()
        {
            string currentRuntimeIdentifier = GetRuntimeIdentifier();

            return GetRuntimeFallbacks(currentRuntimeIdentifier);
        }

        public static List<string> GetRuntimeFallbacks(string rid)
        {
            if (rid == null)
            {
                throw new ArgumentNullException(nameof(rid));
            }

            RuntimeFallbacks fallbacks = DependencyContext.Default
                .RuntimeGraph
                .FirstOrDefault(f => string.Equals(f.Runtime, rid, StringComparison.OrdinalIgnoreCase))
                ?? GetDefaultRuntimeFallbacks(rid)
                ?? new RuntimeFallbacks("any");

            var rids = new List<string> { fallbacks.Runtime };
            rids.AddRange(fallbacks.Fallbacks);
            return rids;
        }

        /// <summary>
        /// Checks if the string is in assembly representation format.
        /// </summary>
        /// <param name="assemblyFormatString"> string representing assembly information</param>
        /// <returns> bool if string in was in proper assembly representation format. </returns>
        public static bool IsAssemblyReferenceFormat(string assemblyFormatString)
        {
            return assemblyFormatString != null && assemblyFormatString.StartsWith(AssemblyNamePrefix);
        }

        /// <summary>
        /// Gets the Assembly name from the assembly path string, if in the expected format for an assembly reference.
        /// </summary>
        /// <param name="assemblyFormatString"> The assembly name string in the expected format. </param>
        /// <returns> bool if the string was in the proper assembly format. </returns>
        public static bool TryGetAssemblyReference(string assemblyFormatString, out string assemblyName)
        {
            assemblyName = null;

            var isSharedAssembly = IsAssemblyReferenceFormat(assemblyFormatString);
            if (isSharedAssembly)
            {
                assemblyName = assemblyFormatString.Substring(AssemblyNamePrefix.Length);
            }

            return isSharedAssembly;
        }

        private static string GetRuntimeIdentifier() => _runtimeIdentifier ??= AppContext.GetData("RUNTIME_IDENTIFIER") as string ?? "unknown";
    }
}
