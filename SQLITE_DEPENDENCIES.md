# SQLite dependencies

UDS includes SQLite 3.53.4 for Windows x64, and two privately named SQLitePCLRaw 3.0.5 assemblies. The mod resolves managed assemblies beside its own DLL through Duckov's `Assembly.LoadFrom` context. It loads the native DLL by a verified full path with `LoadLibraryExW`, restricted to the DLL directory and System32; it does not change PATH or install a Unity plugin globally. Failure to load or verify SQLite prevents profile activation rather than falling back to a second writable format.

| File | SHA-256 |
| --- | --- |
| sqlite3.dll | ab57d0437795ecc757cb693f32ea224173fa9856594d95cfa6b5033e645cd1ec |
| UdsPrototype.SQLiteRaw.Core.dll | 8db3fbec9933c6c95b8253439d5fbe93c109484a79bb44054b050df75fde9880 |
| UdsPrototype.SQLiteRaw.Provider.dll | b67049204702ef53ca9799d5416fa1ce344e2f984bbf7b079bf2737a00c55cc7 |

SQLite source identity: `2026-07-24 19:02:57 bf7c7f30031888f4e796e429ab3978879485813aaca6f641c7b33e4e09459bcc`. The native library is the pinned official [Windows x64 SQLite distribution](https://www.sqlite.org/download.html). SQLite is [public domain](https://www.sqlite.org/copyright.html).

The provider assemblies contain eleven unmodified upstream source files from [SQLitePCLRaw commit ed046114d5a30534e13294d94d78eb73de896ad4](https://github.com/ericsink/SQLitePCL.raw/tree/ed046114d5a30534e13294d94d78eb73de896ad4), retargeted to netstandard2.1. This uses Unity's available Span API and avoids shipping a separate System.Memory facade. Their distinct assembly names retain the exact binaries qualified in Duckov/Mono and isolate provider-wide state from other mods; `Prototype` is part of that preserved binary identity, not an experimental runtime toggle. Their Apache 2.0 license and upstream notices are included as `SQLitePCLRaw-LICENSE.txt` and `SQLitePCLRaw-NOTICE.txt`.

`UltimateDuckovStatistics.Sqlite.dll` is UDS-owned storage code under the repository license. The game-supplied Newtonsoft.Json 13.0.2 remains an external reference and is not included. No game, Harmony, Unity, or framework assemblies are bundled.

Database connections use WAL with FULL synchronization. Related changed records, generation metadata and bounded receipts commit together. The primary commit is the durability acknowledgement; there is no independent recovery database. Passive WAL maintenance runs on a separate worker using loading/sleep hints and a long-session fallback. This preserves sync guarantees; it is not a NORMAL/OFF durability tradeoff. Current domain records use typed columns and individually compressed JSON BLOBs; small records remain raw. Framework Deflate compression runs only for changed records on the storage worker and adds no packaged dependency. The [storage format](docs/COMPRESSED_SQLITE_STORAGE.md) advances from 6 to 7 with a one-time transactional conversion and space reclamation before profile opening. Public JSON/CSV formats remain unchanged. Old recovery files are ignored rather than automatically deleted or promoted. JSON and CSV exports remain generated on demand.
