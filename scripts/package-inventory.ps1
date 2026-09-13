function Get-UdsPackageInputs {
    param([string]$RepositoryRoot, [string]$BuildRoot)
    @(
        @{ Source = (Join-Path $RepositoryRoot 'mod/info.ini'); Destination = 'info.ini' },
        @{ Source = (Join-Path $RepositoryRoot 'mod/preview.png'); Destination = 'preview.png' },
        @{ Source = (Join-Path $BuildRoot 'UltimateDuckovStatistics.dll'); Destination = 'UltimateDuckovStatistics.dll' },
        @{ Source = (Join-Path $BuildRoot 'UltimateDuckovStatistics.Core.dll'); Destination = 'UltimateDuckovStatistics.Core.dll' },
        @{ Source = (Join-Path $BuildRoot 'UltimateDuckovStatistics.Sqlite.dll'); Destination = 'UltimateDuckovStatistics.Sqlite.dll' },
        @{ Source = (Join-Path $BuildRoot 'UdsPrototype.SQLiteRaw.Core.dll'); Destination = 'UdsPrototype.SQLiteRaw.Core.dll' },
        @{ Source = (Join-Path $BuildRoot 'UdsPrototype.SQLiteRaw.Provider.dll'); Destination = 'UdsPrototype.SQLiteRaw.Provider.dll' },
        @{ Source = (Join-Path $BuildRoot 'sqlite3.dll'); Destination = 'sqlite3.dll' },
        @{ Source = (Join-Path $RepositoryRoot 'third_party/sqlite/SQLitePCLRaw-LICENSE.txt'); Destination = 'SQLitePCLRaw-LICENSE.txt' },
        @{ Source = (Join-Path $RepositoryRoot 'third_party/sqlite/SQLitePCLRaw-NOTICE.txt'); Destination = 'SQLitePCLRaw-NOTICE.txt' },
        @{ Source = (Join-Path $RepositoryRoot 'SQLITE_DEPENDENCIES.md'); Destination = 'SQLITE_DEPENDENCIES.md' },
        @{ Source = (Join-Path $RepositoryRoot 'INSTALL.md'); Destination = 'INSTALL.md' },
        @{ Source = (Join-Path $RepositoryRoot 'LICENSE'); Destination = 'LICENSE' }
    )
}

function Test-UdsPinnedDependencies {
    param([string]$Directory)
    $expected = @{
        'sqlite3.dll' = 'ab57d0437795ecc757cb693f32ea224173fa9856594d95cfa6b5033e645cd1ec'
        'UdsPrototype.SQLiteRaw.Core.dll' = '8db3fbec9933c6c95b8253439d5fbe93c109484a79bb44054b050df75fde9880'
        'UdsPrototype.SQLiteRaw.Provider.dll' = 'b67049204702ef53ca9799d5416fa1ce344e2f984bbf7b079bf2737a00c55cc7'
    }
    foreach ($name in $expected.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash -ne $expected[$name]) {
            throw "Pinned dependency hash differs: $name"
        }
    }
}
