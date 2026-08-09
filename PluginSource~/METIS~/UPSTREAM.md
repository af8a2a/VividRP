# METIS source

VividRP vendors the following unmodified upstream source snapshots. Git
metadata is intentionally omitted.

- METIS: https://github.com/KarypisLab/METIS
  - tag: `v5.2.1`
  - commit: `f5ae915a84d3bbf1508b529af90292dd7085b9ec`
  - license: Apache-2.0 (`Source/LICENSE`)
- GKlib: https://github.com/KarypisLab/GKlib
  - commit: `3b7d61b9f885063c89901f3901fb4426f9cfb58f`
  - licenses: Apache-2.0 AND LGPL-2.1-or-later AND BSD-3-Clause
    (`GKlib/LICENSE.txt`, `GKlib/LICENSES.md`, and `GKlib/LICENSES/`)

The VividRP CMake wrapper builds a Windows x86_64 shared library with 32-bit
METIS indices and 32-bit floating-point values. These settings match
`METISBindings.cs`.

Run `PluginSource~/Build-GeometryPlugins.ps1` from the package root to build
the Release DLL and copy it to the Unity plugin directory.
