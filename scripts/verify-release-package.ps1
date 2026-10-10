# Shared by the pack script's repository-version mode (ordinary CI and release).
function Assert-ReleasePackage {
    param($Package, [string] $Path, [string] $ExpectedCommit)

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
                $buffer = [IO.MemoryStream]::new()
                $stream = $entry.Open()
                try {
                    $stream.CopyTo($buffer)
                    $buffer.Position = 0
                    if ($isSymbol) {
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
                                throw "Forbidden assembly/type reference in '$($entry.FullName)'."
                            }
                        }
                        finally { $pe.Dispose() }
                    }
                }
                finally { $stream.Dispose(); $buffer.Dispose() }
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
