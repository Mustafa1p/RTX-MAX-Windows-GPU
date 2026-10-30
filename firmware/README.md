# Firmware source snapshot

Source from craftingmod/rtx30-unlock at commit
`5c076dc4ee7fa369639669fa591ac74d8f6dfcd8`, plus the tested local Palit
direct GOP/TextOut and fixed internal boot port. Read [the build guide](../docs/BUILD.md).

Use `tools/Build-Firmware.ps1` from the repository root for this port.
The preserved upstream `bun run build` targets its original default mode; it is
not the Palit-specific build command. The public internal GUID header deliberately
refuses compilation until the build tool generates local identities.

No firmware payloads, GPU ROM dumps, runtime profiles, or ready-to-boot images are
distributed. Existing copyright notices and third-party licenses are preserved.
