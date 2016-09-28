if (!$PSVersionTable.PSEdition -or $PSVersionTable.PSEdition -eq "Desktop") {
    Add-Type -Path "$PSScriptRoot/bin/Desktop/Microsoft.PowerShell.EditorServices.dll"
    Add-Type -Path "$PSScriptRoot/bin/Desktop/Microsoft.PowerShell.EditorServices.Host.dll"
}
else {
    Add-Type -Path "$PSScriptRoot/bin/Nano/Microsoft.PowerShell.EditorServices.Nano.dll"
}

function New-EditorServicesHost {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [string]
        $HostName,

        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [string]
        $HostProfileId,

        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [string]
        $HostVersion,

        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [int]
        $LanguageServicePort,

        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [int]
        $DebugServicePort,

        [Parameter(Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        $LogPath,

        [ValidateSet("Normal", "Verbose", "Error")]
        $LogLevel = "Normal"
    )

    $editorServicesHost = $null
    $hostDetails = New-Object Microsoft.PowerShell.EditorServices.Session.HostDetails @($HostName, $HostProfileId, (New-Object System.Version @($HostVersion)))

    try {

        # Build the profile paths using the root paths of the current $profile variable
        $profilePaths = New-Object Microsoft.PowerShell.EditorServices.Session.ProfilePaths @(
            $hostDetails.ProfileId,
            [System.IO.Path]::GetDirectoryName($profile.AllUsersAllHosts),
            [System.IO.Path]::GetDirectoryName($profile.CurrentUserAllHosts));

		$hostConfiguration = New-Object Microsoft.PowerShell.EditorServices.Host.EditorServicesHostConfiguration @($hostDetails)
		$hostConfiguration.LogFilePath = $LogPath
		$hostConfiguration.LogLevel = $LogLevel
		$hostConfiguration.ProfilePaths = $profilePaths
		$hostConfiguration.LanguageServicePort = $LanguageServicePort
		$hostConfiguration.DebugServicePort = $DebugServicePort

        $editorServicesHost =
            New-Object Microsoft.PowerShell.EditorServices.Host.EditorServicesHost @(
                [Runspace]::DefaultRunspace,
                $hostConfiguration)
    }
    catch {
        Write-Error "PowerShell Editor Services host initialization failed, terminating."
        Write-Error $_.Exception
    }

    return $editorServicesHost
}