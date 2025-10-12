# Mikktspace.NET
A .NET / C# wrapper for the Mikktspace tangent generation algorithm

This project lets you use the tangent space generation algorithm by [Morten S. Mikkelsen](https://mmikkelsen3d.blogspot.com/) from C# and other .NET based languages.
It includes a simple .NET Standard wrapper library (using P/Invoke) and corresponding native sources, which expose the algorithm as a shared library.

It includes build scripts and instructions for Windows, Linux and Android (NDK) for the native library.  You can also check releases for pre-built versions.

The API is very simple right now and closely follows the original library - you simply pass callbacks to the generation function which allow it to fetch number of vertices per face (library supports tris and quads) and individual vertex position, normal and texture coordinate (UV), then one of two callbacks for getting the calculated tangents back.

I might add fancier API later on, e.g. using Spans and passing whole arrays to the native library to avoid constant callbacks between Managed and Native, but it's pretty quick even now.

I offer no guarantees/support on this, but hopefully it helps out with your projects! I've written this wrapper for my project NeosVR (www.neosvr.com) where it is actively used (during import, recalculating tangents on existing meshes and for many procedural meshes).

https://github.com/Frooxius/Mikktspace.NET