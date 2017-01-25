//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Utility;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerShell.EditorServices.Console;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using Microsoft.PowerShell.EditorServices.Analysis;

namespace Microsoft.PowerShell.EditorServices
{
    /// <summary>
    /// Provides a high-level service for performing semantic analysis
    /// of PowerShell scripts.
    /// </summary>
    public class AnalysisService : IDisposable
    {
        #region Private Fields

        private string[] activeRules;
        private string settingsPath;
        private IAnalysisServiceProvider analysisServiceProvider;
        private IAnalysisServiceProvider formattingServiceProvider;
        private bool isEnabled { get { return this.analysisServiceProvider != null && this.formattingServiceProvider != null; } }
        private bool useSettingsFile
        {
            get
            {
                return this.settingsPath != null;
            }
        }

        /// <summary>
        /// Defines the list of Script Analyzer rules to include by default if
        /// no settings file is specified.
        /// </summary>
        private static readonly string[] IncludedRules = new string[]
        {
            "PSUseToExportFieldsInManifest",
            "PSMisleadingBacktick",
            "PSAvoidUsingCmdletAliases",
            "PSUseApprovedVerbs",
            "PSAvoidUsingPlainTextForPassword",
            "PSReservedCmdletChar",
            "PSReservedParams",
            "PSShouldProcess",
            "PSMissingModuleManifestField",
            "PSAvoidDefaultValueSwitchParameter",
            "PSUseDeclaredVarsMoreThanAssigments"
        };

        #endregion // Private Fields


        #region Properties

        /// <summary>
        /// Set of PSScriptAnalyzer rules used for analysis
        /// </summary>
        public string[] ActiveRules
        {
            get
            {
                return activeRules;
            }

            set
            {
                activeRules = value;
            }
        }

        /// <summary>
        /// Gets or sets the path to a settings file (.psd1)
        /// containing PSScriptAnalyzer settings.
        /// </summary>
        public string SettingsPath
        {
            get
            {
                return settingsPath;
            }
            set
            {
                settingsPath = value;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an instance of the AnalysisService class.
        /// </summary>
        /// <param name="consoleHost">An object that implements IConsoleHost in which to write errors/warnings
        /// from analyzer.</param>
        /// <param name="settingsPath">Path to a PSScriptAnalyzer settings file.</param>
        public AnalysisService(IConsoleHost consoleHost, string settingsPath = null)
        {
            try
            {
                this.SettingsPath = settingsPath;
                this.ActiveRules = IncludedRules.ToArray();
                this.analysisServiceProvider = new AnalysisServiceProvider();
                this.formattingServiceProvider = new AnalysisServiceProvider();
            }
            catch (Exception e)
            {
                var sb = new StringBuilder();
                sb.AppendLine("PSScriptAnalyzer cannot be imported, AnalysisService will be disabled.");
                sb.AppendLine(e.Message);
                Logger.Write(LogLevel.Warning, sb.ToString());
            }
        }

        #endregion // constructors

        #region Public Methods

        /// <summary>
        /// Perform semantic analysis on the given ScriptFile and returns
        /// an array of ScriptFileMarkers.
        /// </summary>
        /// <param name="file">The ScriptFile which will be analyzed for semantic markers.</param>
        /// <returns>An array of ScriptFileMarkers containing semantic analysis results.</returns>
        public async Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file)
        {
            //return await GetSemanticMarkersAsync(file, activeRules, settingsPath);
            if (isEnabled)
            {
                if (useSettingsFile)
                {
                    return await analysisServiceProvider.GetSemanticMarkersAsync(file, this.settingsPath);
                }

                return await analysisServiceProvider.GetSemanticMarkersAsync(
                    file,
                    GetPSSASettingsHashtableFromActiveRules());

            }

            return new ScriptFileMarker[0];
        }

        /// <summary>
        /// Perform semantic analysis on the given ScriptFile with the given settings.
        /// </summary>
        /// <param name="file">The ScriptFile to be analyzed.</param>
        /// <param name="settings">ScriptAnalyzer settings</param>
        /// <returns></returns>
        public async Task<ScriptFileMarker[]> GetSemanticMarkersAsync(ScriptFile file, Hashtable settings)
        {
            if (isEnabled)
            {
                return await analysisServiceProvider.GetSemanticMarkersAsync(file, settings);
            }

            return new ScriptFileMarker[0];
        }

        public async Task<ScriptFileMarker[]> GetSemanticMarkersForFormattingAsync(ScriptFile file, Hashtable settings)
        {
            //return await GetSemanticMarkersAsync<Hashtable>(file, null, settings);
            if (isEnabled)
            {
                return await formattingServiceProvider.GetSemanticMarkersAsync(file, settings);
            }

            return new ScriptFileMarker[0];
        }



        /// <summary>
        /// Returns a list of builtin-in PSScriptAnalyzer rules
        /// </summary>
        public IEnumerable<string> GetPSScriptAnalyzerRules()
        {
            var task = Task.Run(this.analysisServiceProvider.GetAnalysisRules);
            task.Wait();
            return task.Result;
        }

        /// <summary>
        /// Construct a PSScriptAnalyzer settings hashtable
        /// </summary>
        /// <param name="ruleSettingsMap">A settings hashtable</param>
        /// <returns></returns>
        public Hashtable GetPSSASettingsHashtable(IDictionary<string, Hashtable> ruleSettingsMap)
        {
            var hashtable = new Hashtable();
            var ruleSettingsHashtable = new Hashtable();

            hashtable["IncludeRules"] = ruleSettingsMap.Keys.ToArray<object>();
            hashtable["Rules"] = ruleSettingsHashtable;
            foreach (var kvp in ruleSettingsMap)
            {
                ruleSettingsHashtable.Add(kvp.Key, kvp.Value);
            }

            return hashtable;
        }

        /// <summary>
        /// Disposes the runspace being used by the analysis service.
        /// </summary>
        public void Dispose()
        {
            if (isEnabled)
            {
                this.analysisServiceProvider.Dispose();
                this.formattingServiceProvider.Dispose();
            }
        }

        #endregion // public methods

        #region Private Methods

        private Hashtable GetPSSASettingsHashtableFromActiveRules()
        {
            var hashtable = new Hashtable();
            hashtable["IncludeRules"] = this.ActiveRules.ToArray<object>();
            return hashtable;
        }

        #endregion //private methods
    }
}
