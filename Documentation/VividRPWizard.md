# VividRP Wizard

Open **Window > Rendering > VividRP Wizard** to configure the basic Windows rendering requirements for VividRP.

The Wizard can:

- disable automatic graphics API selection for Standalone Windows 64 and place Direct3D12 first;
- configure the active Build Profile to use DXC for shaders targeting Direct3D12.

DXC selection is stored per Build Profile. Create or activate a Windows Build Profile before fixing that item. If the Editor is running with another graphics API after Direct3D12 is configured, restart the Editor to apply the graphics device change.
