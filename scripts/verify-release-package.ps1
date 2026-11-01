# Shared by the pack script's repository-version mode (ordinary CI and release).
# Libraries (lib/<tfm>/<Id>.dll plus a snupkg) and .NET tool packages (PackAsTool) follow separate routes.

# A portable PDB must be readable; an assembly must not reference test, sample, diagnostics or application code.
function Assert-BinaryEntry {
    param($Entry, [bool] $IsSymbol)

    $buffer = [IO.MemoryStream]::new()
    $stream = $Entry.Open()
    try {
        $stream.CopyTo($buffer)
        $buffer.Position = 0
        if ($IsSymbol) {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($buffer)
            try { [void] $provider.GetMetadataReader() } finally { $provider.Dispose() }
        }
        else {
            $pe = [System.Reflection.PortableExecutable.PEReader]::new($buffer)
            try {
                $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                $names = @(
                    foreach ($handle in $reader.AssemblyReferences) {
                        $reader.GetString($reader.GetAssemblyReference($handle).Name)
                    }
                    foreach ($handle in $reader.TypeReferences) {
                        $type = $reader.GetTypeReference($handle)
                        $reader.GetString($type.Namespace) + '.' + $reader.GetString($type.Name)
                    }
                    foreach ($handle in $reader.TypeDefinitions) {
                        $type = $reader.GetTypeDefinition($handle)
                        $reader.GetString($type.Namespace) + '.' + $reader.GetString($type.Name)
                    }
                )
                if (@($names | Where-Object { $_ -match 'TSharpCommander|TSharpVision\.(Diagnostics|Tests?|TestSupport|Samples)(?:\.|$)' }).Count) {
                    throw "Forbidden assembly/type reference in '$($Entry.FullName)'."
                }
            }
            finally { $pe.Dispose() }
        }
    }
    finally { $stream.Dispose(); $buffer.Dispose() }
}

function Assert-ReleasePackage {
    param($Package, [string] $Path, [string] $ExpectedCommit)

    if ($Package.IsTool) {
        Assert-ReleaseToolPackage -Package $Package -Path $Path -ExpectedCommit $ExpectedCommit
        return
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $metadata = (Read-Nuspec -PackagePath $Path).Metadata
        $properties = $Package.Properties
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or $repository.url -ne $properties.RepositoryUrl -or
            $repository.type -ne $properties.RepositoryType) {
            throw "Invalid repository metadata in '$Path'."
        }
        if ($ExpectedCommit -and $repository.GetAttribute('commit') -ne $ExpectedCommit) {
            throw "Repository commit in '$Path' does not match the dispatch commit."
        }
        $groups = @($metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']") |
            ForEach-Object { $_.GetAttribute('targetFramework') } | Sort-Object -Unique)
        $frameworks = @((($properties.TargetFrameworks, $properties.TargetFramework -join ';') -split ';') |
            Where-Object { $_ } | Sort-Object -Unique)
        if (Compare-Object $frameworks $groups) {
            throw "Unexpected target frameworks in '$Path': $($groups -join ', ')."
        }
        $isSymbol = [IO.Path]::GetExtension($Path) -eq '.snupkg'
        if (-not $isSymbol) {
            $license = $metadata.SelectSingleNode("*[local-name()='license']")
            $readme = $metadata.SelectSingleNode("*[local-name()='readme']")
            if ($null -eq $license -or $license.type -ne 'expression' -or
                $license.InnerText -ne $properties.PackageLicenseExpression -or
                $null -eq $readme -or $readme.InnerText -ne $properties.PackageReadmeFile -or
                $null -eq $archive.GetEntry($properties.PackageReadmeFile) -or
                $archive.GetEntry($properties.PackageReadmeFile).Length -eq 0 -or
                $null -eq $archive.GetEntry('LICENSE')) {
                throw "Missing or incorrect license/README in '$Path'."
            }
        }
        foreach ($dependency in $metadata.SelectNodes(".//*[local-name()='dependency']")) {
            if ($dependency.id -match 'TSharpCommander|\.Diagnostics(?:\.|$)|\.Tests?(?:\.|$)|\.Samples(?:\.|$)') {
                throw "Forbidden dependency '$($dependency.id)' in '$Path'."
            }
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match 'TSharpCommander|TSharpVision\.(Diagnostics|Tests?|TestSupport|Samples)(?:[./]|$)') {
                throw "Forbidden payload '$($entry.FullName)' in '$Path'."
            }
            if ($entry.FullName -match '\.(dll|exe|pdb)$') {
                $extension = [IO.Path]::GetExtension($entry.FullName)
                $expectedExtension = if ($isSymbol) { '.pdb' } else { '.dll' }
                $allowed = @($frameworks | ForEach-Object { "lib/$_/$($Package.PackageId)$expectedExtension" })
                if ($entry.FullName -notin $allowed) {
                    throw "Unexpected binary '$($entry.FullName)' in '$Path'."
                }
                Assert-BinaryEntry -Entry $entry -IsSymbol $isSymbol
            }
        }
        foreach ($framework in $frameworks) {
            $extension = if ($isSymbol) { 'pdb' } else { 'dll' }
            if ($null -eq $archive.GetEntry("lib/$framework/$($Package.PackageId).$extension")) {
                throw "Missing $extension for '$framework' in '$Path'."
            }
        }
    }
    finally { $archive.Dispose() }
}

# .NET tool package: one DotnetTool command, framework-dependent payload under tools/<tfm>/any, no NuGet
# dependencies (project references are bundled) and no symbol package (the PDBs ship in the tool payload).
function Assert-ReleaseToolPackage {
    param($Package, [string] $Path, [string] $ExpectedCommit)

    if ([IO.Path]::GetExtension($Path) -ne '.nupkg') {
        throw "Tool package '$($Package.PackageId)' must not produce '$Path'."
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $metadata = (Read-Nuspec -PackagePath $Path).Metadata
        $properties = $Package.Properties
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or $repository.url -ne $properties.RepositoryUrl -or
            $repository.type -ne $properties.RepositoryType) {
            throw "Invalid repository metadata in '$Path'."
        }
        if ($ExpectedCommit -and $repository.GetAttribute('commit') -ne $ExpectedCommit) {
            throw "Repository commit in '$Path' does not match the dispatch commit."
        }
        $types = @($metadata.SelectNodes("*[local-name()='packageTypes']/*[local-name()='packageType']") |
            ForEach-Object { $_.GetAttribute('name') })
        if (($types -join ',') -ne 'DotnetTool') {
            throw "Tool package '$Path' must have exactly the DotnetTool package type; found '$($types -join ', ')'."
        }
        if ($metadata.SelectNodes(".//*[local-name()='dependency']").Count -or
            $metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']").Count) {
            throw "Tool package '$Path' must not declare NuGet dependencies."
        }
        $license = $metadata.SelectSingleNode("*[local-name()='license']")
        $readme = $metadata.SelectSingleNode("*[local-name()='readme']")
        if ($null -eq $license -or $license.type -ne 'expression' -or
            $license.InnerText -ne $properties.PackageLicenseExpression -or
            $null -eq $readme -or $readme.InnerText -ne $properties.PackageReadmeFile -or
            $null -eq $archive.GetEntry($properties.PackageReadmeFile) -or
            $archive.GetEntry($properties.PackageReadmeFile).Length -eq 0 -or
            $null -eq $archive.GetEntry('LICENSE')) {
            throw "Missing or incorrect license/README in '$Path'."
        }

        $framework = $properties.TargetFramework
        if (-not $framework -or $properties.TargetFrameworks) {
            throw "Tool package '$($Package.PackageId)' must target exactly one framework."
        }
        $payload = "tools/$framework/any/"
        $settingsEntry = $archive.GetEntry("${payload}DotnetToolSettings.xml")
        if ($null -eq $settingsEntry) { throw "Missing ${payload}DotnetToolSettings.xml in '$Path'." }
        $reader = [IO.StreamReader]::new($settingsEntry.Open())
        try { [xml] $settings = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $commands = @($settings.SelectNodes('/DotNetCliTool/Commands/Command'))
        $entryPoint = "$($properties.AssemblyName).dll"
        if ($commands.Count -ne 1 -or $commands[0].Name -cne $properties.ToolCommandName -or
            $commands[0].EntryPoint -cne $entryPoint -or $commands[0].Runner -ne 'dotnet') {
            throw "DotnetToolSettings.xml in '$Path' must declare one '$($properties.ToolCommandName)' command running '$entryPoint'."
        }
        foreach ($required in @($entryPoint, "$($properties.AssemblyName).runtimeconfig.json", "$($properties.AssemblyName).deps.json")) {
            if ($null -eq $archive.GetEntry("$payload$required")) { throw "Missing $payload$required in '$Path'." }
        }

        # Only the tool assembly and the TSharpVision libraries it bundles through project references.
        $assemblies = @($properties.AssemblyName) + @($Package.BundledPackages)
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match 'TSharpCommander|TSharpVision\.(Diagnostics|Tests?|TestSupport|Samples)(?:[./]|$)') {
                throw "Forbidden payload '$($entry.FullName)' in '$Path'."
            }
            if ($entry.FullName -match '\.(dll|exe|pdb)$') {
                $extension = [IO.Path]::GetExtension($entry.FullName)
                $allowed = @($assemblies | Where-Object { $extension -ne '.exe' } | ForEach-Object { "$payload$_$extension" })
                if ($entry.FullName -cnotin $allowed) {
                    throw "Unexpected binary '$($entry.FullName)' in '$Path'."
                }
                Assert-BinaryEntry -Entry $entry -IsSymbol ($extension -eq '.pdb')
            }
            elseif ($entry.FullName -match '^(lib|ref|runtimes|build|buildTransitive|analyzers|content|contentFiles)/') {
                throw "Unexpected library asset '$($entry.FullName)' in tool package '$Path'."
            }
        }
    }
    finally { $archive.Dispose() }
}
