# Shared helpers for the publish scripts. Dot-source this file.

# The .NET SDK's publish copy step fails (MSB3094) when the publish directory
# contains an apostrophe, as a checkout under "Ray's Loader" does. Publish to a
# fresh staging directory in the system temp folder, then move the output into
# place.
function Invoke-StagedPublish {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string[]]$PublishArguments
    )

    $stagingRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $staging = Join-Path $stagingRoot ('RaysLoaderPublish-' + [Guid]::NewGuid().ToString('N'))
    if ($staging.Contains("'")) {
        throw "The temp folder path contains an apostrophe, which dotnet publish cannot handle: $staging"
    }

    try {
        dotnet publish $ProjectPath @PublishArguments -o $staging
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

        $parent = Split-Path -Parent $Destination
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Recurse -Force
        }
        # Copy rather than move: temp and the repository can be on different volumes.
        Copy-Item -LiteralPath $staging -Destination $Destination -Recurse
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}
