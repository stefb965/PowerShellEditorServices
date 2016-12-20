//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

namespace Microsoft.PowerShell.EditorServices.Templates
{
    /// <summary>
    /// Provides details about the result of the creation of a file
    /// or project from a template.
    /// </summary>
    public class TemplateResult
    {
        /// <summary>
        /// Gets or sets a boolean which is true if creation was successful.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Gets or sets the template path which was used in creation.
        /// </summary>
        public string TemplatePath { get; set; }

        /// <summary>
        /// Gets or sets the destination path where the file (or files) were created.
        /// </summary>
        public string DestinationPath { get; set; }

        /// <summary>
        /// Gets or sets the array of file paths that were created.
        /// </summary>
        public string[] CreatedFiles { get; set; }

        /// <summary>
        /// Gets or sets the array of file paths that were updated.
        /// </summary>
        public string[] UpdatedFiles { get; set; }

        /// <summary>
        /// Gets or sets the list of modules that will need to be installed for
        /// the created file or project to be fully functional.
        /// </summary>
        public string[] MissingModules { get; set; }
    }
}
