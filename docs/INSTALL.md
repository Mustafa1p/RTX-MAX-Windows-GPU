# Installation options

[العربية](../README.ar.md) · [Compatibility](COMPATIBILITY.md) · [Recovery](RECOVERY.md)

The reference PC already has a verified internal automatic installation. This public folder is a source/tool/evidence distribution, not a copy of that PC's boot partition. No firmware binaries, runtime profile, GPU ROM dump or private partition identifiers are shipped.

## Option 1 — health tools only

Extract the repository. Run `Check-RTXMAX.ps1` or the colored menu `RTX-MAX.ps1` in PowerShell. Administrator mode improves coverage; normal-user mode reports inaccessible checks as unknown. No Python or firmware installation is needed for this option.

```powershell
.\Install-RTXMAX.ps1 -Action Check
```

This action only reads diagnostics and writes its local report.

## Option 2 — manual USB experiment

This is an expert workflow for a separately qualified PC, not automatic support for an identically named card.

1. Record a stock compute baseline and driver/VBIOS/power settings. Save the BitLocker recovery key privately somewhere accessible without the PC before changing boot configuration. Never put it in this repository or an issue.
2. Build/run the upstream read-only UEFI probe on the target PC and retain its report/PROM privately. Take two matching full cold-boot captures. Review physical display ownership/topology, exact PCI/subsystem/chip identity, framebuffer/BAR geometry and cold register state. The source under `firmware/tools/` contains the probe/profile utilities; its upstream research workflow remains experimental.
3. Generate the local evidence-backed JSON profile with `probe-to-profile.py`, then use `runtime_profile.py` to encode `PROFILE.BIN`. Its check reads the original local evidence paths. Do not fabricate approval flags, copy the reference card's profile or bypass a refusal. This source port implements the tested direct GOP/TextOut layout, not every possible board layout.
4. Obtain the exact firmware assets locally using the preserved upstream extraction instructions at [firmware/tools/firmware/README.md](../firmware/tools/firmware/README.md). Verify the five hashes/sizes in [reference-artifacts.json](../results/reference-artifacts.json) and preserve the NVIDIA license. Extraction is not a hardware-support test.
5. Build the **Manual** port using [BUILD.md](BUILD.md). This requires U and logging; there is no automatic profile creation.
6. Prepare a **new ordinary folder** on a fixed Windows data drive. The following placeholders must be replaced with your local paths:

```powershell
.\Install-RTXMAX.ps1 -Action Stage `
    -BuildDirectory .\build\my-manual-build `
    -ProfilePath C:\LocalEvidence\PROFILE.BIN `
    -AssetsDirectory C:\LocalAssets\firmware-assets `
    -Destination C:\LocalPackages\rtxmax-manual
```

The destination parent must already exist. The staging tool checks EFI/asset hashes and the profile's format/checksum/target; it does **not** approve hardware topology. It refuses existing output folders and direct USB/ESP staging. It does not format or repartition anything.

7. Review the prepared tree, then copy it to a separately prepared spare FAT32 USB drive. `EFI/BOOT/BOOTX64.EFI`, `RTX-UNLOCK/PROFILE.BIN` and all five files under `RTX-UNLOCK/ASSETS/` must be present. Do not overwrite the Windows ESP. Keep a Windows Boot Manager fallback and your recovery key available.
8. Unsigned EFI requires an appropriate Secure Boot configuration. Changing startup/Secure Boot can trigger BitLocker recovery. Review a bounded protection suspension before changing the boot chain, then restore protection after a successful Windows boot. Do not clear the TPM or delete protectors. This package makes none of these changes automatically.
9. Fully shut down and use the motherboard's one-time boot menu for the USB. Avoid Shift+Restart for the controlled test: it intentionally enters Windows Recovery. At the manual tool, use U only after the local preflight is qualified. Retain the transaction/restoration and Windows handoff evidence.
10. Back in Windows, rerun the same correctness-checked benchmark and compare settings/source/library hashes. Test stability before considering internal automatic boot. A successful launch or healthy driver alone is insufficient.

## Option 3 — internal automatic startup

Only proceed after a successful local manual hardware test, correctness checks, display restoration and Windows handoff. The reference installation uses a **dedicated** internal FAT32 GPT ESP, distinct from the Windows ESP. Its exact partition size/layout is a reference, not a command to shrink another user's D: drive.

1. Back up data and review a target-specific partition plan. This package has no disk-format/shrink script. Identify the existing Windows ESP and a separate dedicated non-USB unlock ESP by their actual GPT GUIDs.
2. Build **Internal** mode with those two different local GUIDs. [BUILD.md](BUILD.md) shows the command. The generated image pins both identities and rejects USB sources/foreign partitions. Never reuse another PC's GUID header.
3. Stage the internal build with the same `Install-RTXMAX.ps1 -Action Stage` command. It creates `EFI/PalitUnlock/AUTO.EFI` plus the local profile/assets. The staging manifest is `RTX-UNLOCK/installation.json`; its state is explicitly **staged only, not installed**.
4. Review/copy files to the dedicated ESP. Preserve the normal Windows loader. Register a separate firmware application entry pointing at `\EFI\PalitUnlock\AUTO.EFI` on that partition, using the reviewed local installation procedure. This public tool does not create that entry or make it default.
5. First test it through a one-time firmware selection, with Windows still the persistent default. For diagnostic logging, create an empty `RTX-UNLOCK/AUTOLOG.ON` on the dedicated partition. `DEBUG.ON` requests the manual U override. The normal automatic quiet configuration has neither marker.
6. Verify actual UEFI BootCurrent against the intended internal GPT partition/path, all artifact hashes, the fresh Windows session, relevant errors, correctness and measured gains. Quiet boots do not generate a fresh RUN log; an old RUN directory is not evidence of the new boot.
7. Only after qualification, set that entry first with Windows Boot Manager immediately available as fallback. Follow bounded BitLocker boot-transition handling. Perform a full startup and verify again. Restore protection and check its status after the final intended boot route is established.

The reference PC passed that sequence. Its automatic loader reapplies the unlock at full startup; it is not a permanent VBIOS modification. Sleep/hibernate/resume and driver resets need separate qualification.

## References

- [Microsoft: Secure Boot status checks require elevation](https://learn.microsoft.com/en-us/powershell/module/secureboot/confirm-securebootuefi)
- [Microsoft: bounded BitLocker protection suspension](https://learn.microsoft.com/en-us/powershell/module/bitlocker/suspend-bitlocker)
- [Microsoft: Windows Recovery Environment](https://support.microsoft.com/en-us/windows/experience/backup-recovery/windows-recovery-environment)
- [Microsoft: shutdown command](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown)
- [Upstream source and installation context](https://github.com/craftingmod/rtx30-unlock/tree/5c076dc4ee7fa369639669fa591ac74d8f6dfcd8)
