//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading.Tasks;

namespace Microsoft.PowerShell.EditorServices.Analysis
{
    interface IAnalysisServiceProvider : IDisposable
    {
        Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file);
        Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file, Hashtable settings);
        Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file, string settingsFilePath);

        Task<string[]> GetAnalysisRules();
    }

    public class AnalysisServiceProvider : IAnalysisServiceProvider
    {
        private readonly int maxRunspaces = 1;
        private RunspacePool runspacePool;
        private PSModuleInfo scriptAnalyzerModuleInfo;
        private bool isEnabled { get { return scriptAnalyzerModuleInfo != null; } }

        public AnalysisServiceProvider()
        {
            this.scriptAnalyzerModuleInfo = FindPSScriptAnalyzerModule();
            if (this.scriptAnalyzerModuleInfo == null)
            {
                return;
            }

            var sessionState = InitialSessionState.CreateDefault2();
            sessionState.ImportPSModulesFromPath(this.scriptAnalyzerModuleInfo.ModuleBase);
            this.runspacePool = RunspaceFactory.CreateRunspacePool(sessionState);
            this.runspacePool.SetMaxRunspaces(this.maxRunspaces);
            this.runspacePool.Open();
        }

        public void Dispose()
        {
            this.runspacePool.Dispose();
        }

        public async Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file)
        {
            return await GetScriptFileMarkersAsync<Hashtable>(file, null);
        }

        public async Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file, Hashtable settings)
        {
            return await GetScriptFileMarkersAsync<Hashtable>(file, settings);
        }

        public async Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file, string settingsFilePath)
        {
            return await GetScriptFileMarkersAsync<string> (file, settingsFilePath);
        }

        public async Task<string[]> GetAnalysisRules()
        {
            List<string> ruleNames = new List<string>();
            if (isEnabled)
            {
                var ruleObjects = await InvokePowerShellAsync(
                    "Get-ScriptAnalyzerRule",
                    new Dictionary<string, object>());
                foreach (dynamic rule in ruleObjects)
                {
                    var ruleName = rule.RuleName as string;
                    if (ruleName != null)
                    {
                        ruleNames.Add(rule.RuleName);
                    }
                }
            }

            return ruleNames.ToArray();
        }

        private async Task<ScriptFileMarker[]> GetScriptFileMarkersAsync<TSettings>(
            ScriptFile file,
            TSettings settings) where TSettings : class
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            var scriptFileMarkers = await GetDiagnosticRecordsAsync(file, settings);
            return scriptFileMarkers.Select(ScriptFileMarker.FromDiagnosticRecord).ToArray();
        }

        private async Task<PSObject[]> GetDiagnosticRecordsAsync<TSettings>(
            ScriptFile file,
            TSettings settings) where TSettings : class
        {
            PSObject[] diagnosticRecords = new PSObject[0];

            if (this.isEnabled
                && (typeof(TSettings) == typeof(string)
                    || typeof(TSettings) == typeof(Hashtable)))
            {
                //Use a settings file if one is provided, otherwise use the default rule list.
                diagnosticRecords = await InvokePowerShellAsync(
                    "Invoke-ScriptAnalyzer",
                    new Dictionary<string, object>
                    {
                        { "ScriptDefinition", file.Contents },
                        { "Settings", settings }
                    });
            }

            Logger.Write(
                LogLevel.Verbose,
                String.Format("Found {0} violations", diagnosticRecords.Count()));
            return diagnosticRecords;
        }

        private static PSModuleInfo FindPSScriptAnalyzerModule()
        {
            using (var ps = System.Management.Automation.PowerShell.Create())
            {
                ps.AddCommand("Get-Module")
                  .AddParameter("ListAvailable")
                  .AddParameter("Name", "PSScriptAnalyzer");

                ps.AddCommand("Sort-Object")
                  .AddParameter("Descending")
                  .AddParameter("Property", "Version");

                ps.AddCommand("Select-Object")
                  .AddParameter("First", 1);

                var modules = ps.Invoke<PSModuleInfo>();
                var scriptAnalyzerModuleInfo = modules == null ? null : modules.FirstOrDefault();
                if (scriptAnalyzerModuleInfo != null)
                {
                    Logger.Write(
                        LogLevel.Normal,
                            string.Format(
                                "PSScriptAnalyzer found at {0}",
                                scriptAnalyzerModuleInfo.Path));

                    return scriptAnalyzerModuleInfo;
                }

                Logger.Write(
                    LogLevel.Normal,
                    "PSScriptAnalyzer module was not found.");
                return null;
            }
        }

        private async Task<PSObject[]> InvokePowerShellAsync(
            string command,
            IDictionary<string, object> paramArgMap)
        {
            using (var powerShell = System.Management.Automation.PowerShell.Create())
            {
                powerShell.RunspacePool = this.runspacePool;
                powerShell.AddCommand(command);
                foreach (var kvp in paramArgMap)
                {
                    powerShell.AddParameter(kvp.Key, kvp.Value);
                }

                var objs = await Task.Factory.FromAsync(powerShell.BeginInvoke(), powerShell.EndInvoke);
                if (objs != null)
                {
                    return objs.ToArray();
                }

                return new PSObject[0];
            }
        }
    }
}
